using AppCap;
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

namespace AppCap.Windows;

internal sealed class RecordingWorker : IDisposable
{
    public const string WorkerCommand = "--appcap-record-worker";

    private readonly TargetWindow window;
    private readonly string outputPath;
    private readonly RecordingCaptureTarget recordingTarget;
    private readonly BlockingCollection<Direct3D11CaptureFrame> frames = new(new ConcurrentQueue<Direct3D11CaptureFrame>());
    private readonly TaskCompletionSource firstFrameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Direct3D11CaptureFrame? startFrame;
    private bool disposed;

    private RecordingWorker(TargetWindow window, string outputPath)
    {
        this.window = window;
        this.outputPath = outputPath;
        recordingTarget = new RecordingCaptureTarget(window);
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
        catch (AppCapException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return exception.ExitCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.OperationalError;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        RecordingIpc.RecordingStopRequest? stopRequest = null;
        bool stopAcknowledged = false;
        using CancellationTokenSource captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(window.Target.Name, new WorkerService(recordingTarget, isRecording: true));
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(listenerCancellation.Token);
        Task encode = EncodeAsync(captureCancellation.Token);
        try
        {
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
            }

            // Finish encoding and validate the output *before* acknowledging the stop,
            // so an encode failure or a missing/empty file is reported back to the stop
            // client instead of being hidden behind a premature success acknowledgement.
            await encode.ConfigureAwait(false);
            EnsureOutputFileExists();

            if (stopRequest is not null)
            {
                await stopRequest.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
                stopAcknowledged = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await captureCancellation.CancelAsync().ConfigureAwait(false);
            frames.CompleteAdding();

            // Let the encoder unwind and release the output file before deleting the
            // partial/corrupt recording it may have left behind.
            await DrainEncodeAsync(encode).ConfigureAwait(false);
            DeleteOutputFile();

            if (stopRequest is not null && !stopAcknowledged)
            {
                await stopRequest.FailAsync(exception.Message, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            // If the encoder finished first (e.g. the target window closed), the stop
            // listener is still waiting on its pipe; cancel and drain it so the task is
            // observed and its named-pipe instance is released rather than leaked.
            await listenerCancellation.CancelAsync().ConfigureAwait(false);
            await DrainListenerAsync(waitForStop, stopRequest).ConfigureAwait(false);
            stopRequest?.Dispose();
            DisposeQueuedFrames();
        }
    }

    // Observes the stop-command listener task after it has been cancelled so its
    // named-pipe instance is released and no exception is left unobserved. Any request
    // it produced is disposed unless it is the one the caller already consumed (which
    // is disposed separately).
    private static async Task DrainListenerAsync(Task<RecordingIpc.RecordingStopRequest> waitForStop, RecordingIpc.RecordingStopRequest? consumed)
    {
        RecordingIpc.RecordingStopRequest request;
        try
        {
            request = await waitForStop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (!ReferenceEquals(request, consumed))
        {
            request.Dispose();
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
            throw new AppCapException("Recording capture is not supported on this Windows version.");
        }

        GraphicsCaptureItem item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new AppCapException("Target window could not be captured.");
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

            // Serve any pending screenshot from this live frame before handing it to the
            // encoder, so a screenshot taken while recording reuses this capture session.
            recordingTarget.OfferFrame(frame);

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
            throw new AppCapException("Recording encoding failed.");
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
            throw new AppCapException($"Recording did not produce an output file at '{fullOutputPath}'.");
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
        // Hold the first captured frame so it both anchors the media timeline and is
        // emitted as the first sample. A static window may produce only this one frame,
        // so discarding it here would leave the encoder with no samples at all.
        startFrame = TakeNextFrame();
        args.Request.SetActualStartPosition(startFrame?.SystemRelativeTime ?? TimeSpan.Zero);
    }

    private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        // Emit the held start frame first, then block for each subsequent captured frame
        // (or until capture stops). This paces the encoder to the capture rate without
        // busy-waiting. Starting and SampleRequested are serialized by the
        // MediaStreamSource, so reading startFrame here needs no synchronization.
        Direct3D11CaptureFrame? frame = startFrame ?? TakeNextFrame();
        startFrame = null;
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
            throw new AppCapException("Recording did not capture any frames.");
        }
    }

    private void DisposeQueuedFrames()
    {
        // Safe to touch startFrame here: callers run only after the encode task has
        // completed, so the MediaStreamSource is no longer raising frame callbacks.
        Direct3D11CaptureFrame? pending = startFrame;
        startFrame = null;
        pending?.Dispose();

        while (frames.TryTake(out Direct3D11CaptureFrame? frame))
        {
            frame.Dispose();
        }
    }

    private static RecordingWorker Create(IReadOnlyList<string> args)
    {
        Dictionary<string, string> options = ParseOptions(args.Skip(1));
        AppCapTargetConfig application = new() { Name = options["application-name"], Id = options["aumid"] };
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
                throw new AppCapException("Recording worker arguments are invalid.");
            }

            options[key[2..]] = enumerator.Current;
        }

        foreach (string required in new[] { "target-name", "application-name", "aumid", "window-handle", "output" })
        {
            if (!options.ContainsKey(required))
            {
                throw new AppCapException("Recording worker arguments are invalid.");
            }
        }

        return options;
    }
}