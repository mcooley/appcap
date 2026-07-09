using AppCap;
using AppCap.Protocol.Target;
using System.Collections.Concurrent;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Media.Core;
using global::Windows.Media.MediaProperties;
using global::Windows.Media.Transcoding;
using global::Windows.Storage;
using global::Windows.Storage.Streams;

namespace AppCap.Windows;

// A single live recording owned by the worker: it captures a target window's frames,
// feeds their GPU surfaces straight into the media encoder (the optimized in-proc frame
// handoff), and finalizes the MP4 when the recording stops or the window closes. The
// machine-wide WorkerHost keeps one of these per recording target and multiplexes many at
// once, so each session is fully self-contained and independently cancellable.
internal sealed class RecordingSession : IDisposable
{
    private readonly TargetWindow window;
    private readonly string outputPath;
    private readonly RecordingCaptureTarget recordingTarget;
    private readonly BlockingCollection<Direct3D11CaptureFrame> frames = new(new ConcurrentQueue<Direct3D11CaptureFrame>());
    private readonly TaskCompletionSource firstFrameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource captureCancellation;
    private Task encodeTask = Task.CompletedTask;
    private Task completion = Task.CompletedTask;
    private Direct3D11CaptureFrame? startFrame;
    private int stopRequested;
    private int stopDiscard;
    private bool disposed;

    public RecordingSession(TargetWindow window, string outputPath, CancellationToken cancellationToken)
    {
        this.window = window;
        this.outputPath = Path.GetFullPath(outputPath);
        recordingTarget = new RecordingCaptureTarget(window);
        captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    // Serves screenshots for this recording from its live capture session, so a screenshot
    // taken while recording never starts a second capture session.
    public ITarget Target => recordingTarget;

    // Completes when the recording has fully finished and its output has been finalized
    // (saved and validated, or discarded). Faults if saving the output failed.
    public Task Completion => completion;

    // Starts capturing and encoding, returning once the recording is confirmed running (its
    // first frame has been captured). Throws AppCapException if the target cannot be
    // captured or no frames arrive.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        encodeTask = EncodeAsync(captureCancellation.Token);

        Task confirmed = await Task.WhenAny(firstFrameArrived.Task, encodeTask).ConfigureAwait(false);
        if (confirmed == encodeTask)
        {
            // The encode ended before any frame arrived: surface its failure (or, if it
            // somehow completed cleanly, report that nothing was captured).
            await encodeTask.ConfigureAwait(false);
            throw new AppCapException("Recording did not capture any frames.");
        }

        completion = FinalizeWhenDoneAsync();
    }

    // Requests the recording stop (saving, or discarding when discard is true) and awaits
    // its finalization. Idempotent: concurrent or repeated calls all await the same
    // completion. Throws if saving the output failed.
    public async Task<bool> StopAsync(bool discard, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref stopRequested, 1, 0) == 0)
        {
            Volatile.Write(ref stopDiscard, discard ? 1 : 0);
            SignalStop(discard);
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Signals the encode to stop, tolerating a session whose capture pipeline has already
    // fully torn down (for example, the window closed at the same moment): in that case the
    // collection/token may already be completed or disposed and there is simply nothing left
    // to signal.
    private void SignalStop(bool discard)
    {
        try
        {
            if (discard)
            {
                captureCancellation.Cancel();
            }

            frames.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    // Awaits the encode to finish (via a stop, a window close, or a discard cancellation)
    // and then finalizes the output: validates a saved file, or deletes a discarded or
    // failed one. Faults so a stopping client sees a save failure.
    private async Task FinalizeWhenDoneAsync()
    {
        try
        {
            await encodeTask.ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref stopDiscard) == 1)
        {
            // The recording was discarded: the encode was cancelled, which is expected.
            DeleteOutputFile();
            return;
        }
        catch (Exception)
        {
            DeleteOutputFile();
            throw;
        }

        if (Volatile.Read(ref stopDiscard) == 1)
        {
            DeleteOutputFile();
            return;
        }

        EnsureOutputFileExists();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        captureCancellation.Dispose();
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
                // Covers ObjectDisposedException (a subtype) from a late frame after the
                // capture queue is completed or disposed.
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
        string directory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directory).AsTask(cancellationToken).ConfigureAwait(false);
        StorageFile file = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken).ConfigureAwait(false);
        return await file.OpenAsync(FileAccessMode.ReadWrite).AsTask(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureOutputFileExists()
    {
        FileInfo file = new(outputPath);
        if (!file.Exists || file.Length == 0)
        {
            throw new AppCapException($"Recording did not produce an output file at '{outputPath}'.");
        }
    }

    private void DeleteOutputFile()
    {
        try
        {
            File.Delete(outputPath);
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
}
