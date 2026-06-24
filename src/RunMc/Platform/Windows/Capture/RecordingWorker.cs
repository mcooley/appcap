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
    private readonly Lock pendingFrameLock = new();
    private readonly Lock lastFrameLock = new();
    private Direct3D11CaptureFrame? pendingFrame;
    private FrameLease? lastFrameLease;
    private TimeSpan? firstSampleTime;
    private TimeSpan lastSampleTime;
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
            ClearPendingFrame();
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
        ClearPendingFrame();
        DisposeLastFrame();
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

    private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        Direct3D11CaptureFrame? frame = TakeFrame();
        if (frame is null)
        {
            return;
        }

        lock (pendingFrameLock)
        {
            pendingFrame = frame;
        }
        StoreLastFrame(frame);

        firstSampleTime = frame.SystemRelativeTime;
        lastSampleTime = TimeSpan.Zero;
        args.Request.SetActualStartPosition(TimeSpan.Zero);
    }

    private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        Direct3D11CaptureFrame? newFrame = TakePendingFrame() ?? TakeFrame();
        if (newFrame is not null)
        {
            StoreLastFrame(newFrame);
        }
        else if (frames.IsCompleted)
        {
            args.Request.Sample = null;
            return;
        }
        else
        {
            Thread.Sleep(33);
        }

        FrameLease? lease = GetLastFrameLease();
        Direct3D11CaptureFrame? frame = lease?.AcquireForSample();
        if (lease is null || frame is null)
        {
            args.Request.Sample = null;
            return;
        }

        MediaStreamSample sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, GetSampleTimestamp(frame));
        sample.Duration = TimeSpan.FromMilliseconds(33);
        sample.Processed += (_, _) => lease.Release();
        args.Request.Sample = sample;
    }

    private TimeSpan GetSampleTimestamp(Direct3D11CaptureFrame frame)
    {
        TimeSpan timestamp = frame.SystemRelativeTime - (firstSampleTime ?? frame.SystemRelativeTime);
        if (timestamp <= lastSampleTime)
        {
            timestamp = lastSampleTime + TimeSpan.FromMilliseconds(33);
        }

        lastSampleTime = timestamp;
        return timestamp;
    }

    private Direct3D11CaptureFrame? TakePendingFrame()
    {
        lock (pendingFrameLock)
        {
            Direct3D11CaptureFrame? frame = pendingFrame;
            pendingFrame = null;
            return frame;
        }
    }

    private Direct3D11CaptureFrame? TakeFrame()
    {
        try
        {
            return frames.TryTake(out Direct3D11CaptureFrame? frame, TimeSpan.FromMilliseconds(100)) ? frame : null;
        }
        catch (InvalidOperationException)
        {
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

    private void ClearPendingFrame() => TakePendingFrame();

    private void StoreLastFrame(Direct3D11CaptureFrame frame)
    {
        FrameLease? previous;
        lock (lastFrameLock)
        {
            if (lastFrameLease is not null && lastFrameLease.IsFrame(frame))
            {
                return;
            }

            previous = lastFrameLease;
            lastFrameLease = new FrameLease(frame);
        }

        previous?.Release();
    }

    private FrameLease? GetLastFrameLease()
    {
        lock (lastFrameLock)
        {
            return lastFrameLease;
        }
    }

    private void DisposeLastFrame()
    {
        FrameLease? lease;
        lock (lastFrameLock)
        {
            lease = lastFrameLease;
            lastFrameLease = null;
        }

        lease?.Release();
    }

    // Reference-counts a capture frame so its surface is not disposed while a
    // MediaStreamSample built from it is still in flight in the transcoder. The
    // lease starts with a single reference for the "last frame" slot; each sample
    // adds a reference that is released from the sample's Processed event.
    private sealed class FrameLease
    {
        private readonly Lock gate = new();
        private Direct3D11CaptureFrame? frame;
        private int references = 1;

        public FrameLease(Direct3D11CaptureFrame frame) => this.frame = frame;

        public bool IsFrame(Direct3D11CaptureFrame candidate)
        {
            lock (gate)
            {
                return ReferenceEquals(frame, candidate);
            }
        }

        public Direct3D11CaptureFrame? AcquireForSample()
        {
            lock (gate)
            {
                if (frame is null)
                {
                    return null;
                }

                references++;
                return frame;
            }
        }

        public void Release()
        {
            Direct3D11CaptureFrame? toDispose = null;
            lock (gate)
            {
                if (frame is null)
                {
                    return;
                }

                references--;
                if (references == 0)
                {
                    toDispose = frame;
                    frame = null;
                }
            }

            toDispose?.Dispose();
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