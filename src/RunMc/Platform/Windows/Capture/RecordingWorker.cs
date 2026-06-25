using RunMc;
using System.Collections.Concurrent;
using System.Globalization;
using global::Windows.Foundation;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Media.Core;
using global::Windows.Media.MediaProperties;
using global::Windows.Media.Transcoding;
using global::Windows.Storage;
using global::Windows.Storage.Streams;

namespace RunMc.Windows;

internal sealed class RecordingWorker : IDisposable
{
    public const string WorkerCommand = "--runmc-record-worker";

    private readonly TargetWindow window;
    private readonly string outputPath;
    private readonly BlockingCollection<Direct3D11CaptureFrame> frames = new(new ConcurrentQueue<Direct3D11CaptureFrame>());
    private readonly TaskCompletionSource firstFrameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;

    private RecordingWorker(TargetWindow window, string outputPath)
    {
        this.window = window;
        this.outputPath = outputPath;
    }

    public static bool IsWorkerInvocation(IReadOnlyList<string> args) => args.Count > 0 && args[0] == WorkerCommand;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        try
        {
            using RecordingWorker worker = Create(args);
            await worker.RunAsync(cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.UsageError;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        RecordingIpc.RecordingStopRequest? stopRequest = null;
        bool stopAcknowledged = false;
        using CancellationTokenSource captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(window.Target.Name);
        try
        {
            Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cancellationToken);
            Task encode = EncodeAsync(captureCancellation.Token);

            Task completed = await Task.WhenAny(waitForStop, encode).ConfigureAwait(false);
            if (completed == waitForStop)
            {
                stopRequest = await waitForStop.ConfigureAwait(false);
                if (stopRequest.Mode == RecordingIpc.RecordingStopMode.Discard)
                {
                    await captureCancellation.CancelAsync().ConfigureAwait(false);
                    frames.CompleteAdding();
                    await DrainEncodeAsync(encode).ConfigureAwait(false);
                    DeleteOutputFile();
                    await stopRequest.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
                    stopAcknowledged = true;
                    return;
                }

                frames.CompleteAdding();
                await stopRequest.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
                stopAcknowledged = true;
            }

            await encode.ConfigureAwait(false);
            EnsureOutputFileExists();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await captureCancellation.CancelAsync().ConfigureAwait(false);
            frames.CompleteAdding();
            if (stopRequest is not null && !stopAcknowledged)
            {
                await stopRequest.FailAsync(exception.Message, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            stopRequest?.Dispose();
            DisposeQueuedFrames();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeQueuedFrames();
        frames.Dispose();
    }

    private async Task EncodeAsync(CancellationToken cancellationToken)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new RunMcException("Recording capture is not supported on this Windows version.");
        }

        GraphicsCaptureItem item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new RunMcException("Target window could not be captured.");
        }

        using Direct3DDeviceLease deviceLease = Direct3DDeviceFactory.CreateDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            deviceLease.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            try
            {
                frames.Add(frame, cancellationToken);
                firstFrameArrived.TrySetResult();
            }
            catch (InvalidOperationException)
            {
                frame.Dispose();
            }
            catch (OperationCanceledException)
            {
                frame.Dispose();
            }
        }

        void OnClosed(GraphicsCaptureItem sender, object args)
        {
            frames.CompleteAdding();
        }

        framePool.FrameArrived += OnFrameArrived;
        item.Closed += OnClosed;
        try
        {
            session.IsCursorCaptureEnabled = false;
            session.StartCapture();
            await WaitForFirstFrameAsync(cancellationToken).ConfigureAwait(false);
            await EncodeCaptureFramesAsync(item.Size.Width, item.Size.Height, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
            item.Closed -= OnClosed;
            frames.CompleteAdding();
        }
    }

    private async Task EncodeCaptureFramesAsync(int width, int height, CancellationToken cancellationToken)
    {
        using IRandomAccessStream stream = await CreateOutputStreamAsync(cancellationToken).ConfigureAwait(false);
        VideoEncodingProperties videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        VideoStreamDescriptor videoDescriptor = new(videoProperties);
        MediaStreamSource source = new(videoDescriptor)
        {
            BufferTime = TimeSpan.Zero,
        };
        source.Starting += OnMediaStreamSourceStarting;
        source.SampleRequested += OnMediaStreamSourceSampleRequested;

        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        MediaTranscoder transcoder = new()
        {
            HardwareAccelerationEnabled = true,
        };
        PrepareTranscodeResult prepareResult = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile).AsTask(cancellationToken).ConfigureAwait(false);
        if (!prepareResult.CanTranscode)
        {
            throw new RunMcException("Recording encoding failed.");
        }

        await prepareResult.TranscodeAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IRandomAccessStream> CreateOutputStreamAsync(CancellationToken cancellationToken)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directory).AsTask(cancellationToken).ConfigureAwait(false);
        StorageFile file = await folder.CreateFileAsync(Path.GetFileName(fullOutputPath), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken).ConfigureAwait(false);
        return await file.OpenAsync(FileAccessMode.ReadWrite).AsTask(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureOutputFileExists()
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        FileInfo file = new(fullOutputPath);
        if (!file.Exists || file.Length == 0)
        {
            throw new RunMcException($"Recording did not produce an output file at '{fullOutputPath}'.");
        }
    }

    // Awaits the encode task while a recording is being discarded; the recording is
    // thrown away, so any failure from the cancelled encode is irrelevant.
    private static async Task DrainEncodeAsync(Task encode)
    {
        try
        {
            await encode.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void DeleteOutputFile()
    {
        try
        {
            File.Delete(Path.GetFullPath(outputPath));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        // The first captured frame establishes the start of the media timeline.
        // Every sample carries its own SystemRelativeTime, so the encoder reconstructs
        // the real capture cadence without any artificial pacing.
        using Direct3D11CaptureFrame? frame = TakeNextFrame();
        args.Request.SetActualStartPosition(frame?.SystemRelativeTime ?? TimeSpan.Zero);
    }

    private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        // Block until the next captured frame is available (or capture has stopped).
        // This paces the encoder to the capture rate without busy-waiting.
        Direct3D11CaptureFrame? frame = TakeNextFrame();
        if (frame is null)
        {
            args.Request.Sample = null;
            return;
        }

        // Use the frame's real capture time as the sample timestamp; the encoder
        // derives frame durations from the spacing between consecutive timestamps.
        MediaStreamSample sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, frame.SystemRelativeTime);

        // Keep the frame (and its pooled surface) alive until the encoder has finished
        // with the sample, then dispose it to return the surface to the capture pool.
        sample.Processed += (_, _) => frame.Dispose();
        args.Request.Sample = sample;
    }

    private Direct3D11CaptureFrame? TakeNextFrame()
    {
        try
        {
            return frames.Take();
        }
        catch (InvalidOperationException)
        {
            // The capture queue has been marked complete and fully drained.
            return null;
        }
    }

    private async Task WaitForFirstFrameAsync(CancellationToken cancellationToken)
    {
        Task completed = await Task.WhenAny(firstFrameArrived.Task, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)).ConfigureAwait(false);
        if (completed != firstFrameArrived.Task)
        {
            throw new RunMcException("Recording did not capture any frames.");
        }
    }

    private void DisposeQueuedFrames()
    {
        while (frames.TryTake(out Direct3D11CaptureFrame? frame))
        {
            frame.Dispose();
        }
    }

    private static RecordingWorker Create(IReadOnlyList<string> args)
    {
        Dictionary<string, string> options = ParseOptions(args.Skip(1));
        TargetApplication application = new(options["application-name"], options["package-family-name"], options["aumid"]);
        TargetConfiguration target = new(options["target-name"], [application]);
        TargetWindow window = new(target, application, nint.Parse(options["window-handle"], CultureInfo.InvariantCulture));
        return new RecordingWorker(window, options["output"]);
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        using IEnumerator<string> enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            string key = enumerator.Current;
            if (!key.StartsWith("--", StringComparison.Ordinal) || !enumerator.MoveNext())
            {
                throw new RunMcException("Recording worker arguments are invalid.");
            }

            options[key[2..]] = enumerator.Current;
        }

        foreach (string required in new[] { "target-name", "application-name", "package-family-name", "aumid", "window-handle", "output" })
        {
            if (!options.ContainsKey(required))
            {
                throw new RunMcException("Recording worker arguments are invalid.");
            }
        }

        return options;
    }
}