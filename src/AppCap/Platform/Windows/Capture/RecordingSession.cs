using AppCap.Protocol.Target;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;

namespace AppCap.Windows;

// Owns the graphics-capture lifetime and forwards captured frames to RecordingWriter,
// which independently composes captions/crops and writes the final recording.
internal sealed class RecordingSession : IDisposable
{
    private readonly TargetWindow window;
    private readonly RecordingCaptureTarget recordingTarget;
    private readonly RecordingWriter writer;
    private readonly CancellationTokenSource captureCancellation;
    private readonly CancellationTokenSource timeLimitCancellation = new();
    private readonly TaskCompletionSource finalizationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan timeLimit;
    private readonly bool includeCursor;
    private Task captureTask = Task.CompletedTask;
    private Task completion = Task.CompletedTask;
    private int stopRequested;
    private bool disposed;

    public RecordingSession(TargetWindow window, string outputPath, TimeSpan timeLimit, bool includeCursor, CropRectangle? crop, CancellationToken cancellationToken)
    {
        this.window = window;
        this.timeLimit = timeLimit;
        this.includeCursor = includeCursor;
        recordingTarget = new RecordingCaptureTarget(window);
        writer = new RecordingWriter(outputPath, crop);
        captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public ITarget Target => recordingTarget;

    public Task Completion => completion;

    public void AddCaption(string text) => writer.AddCaption(text);

    public async Task StartAsync(CancellationToken cancellationToken)
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

        Task writerStart = writer.StartAsync(item.Size.Width, item.Size.Height, captureCancellation.Token);
        captureTask = CaptureAsync(item, captureCancellation.Token);
        await writerStart.ConfigureAwait(false);
        completion = CompleteAsync();
        _ = StopAtTimeLimitAsync();
    }

    public async Task<bool> StopAsync(bool discard, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref stopRequested, 1, 0) == 0)
        {
            CancelTimeLimit();
            if (discard)
            {
                captureCancellation.Cancel();
            }

            writer.CompleteFrames();
        }

        try
        {
            await writer.StopAsync(discard, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            finalizationCompleted.TrySetResult();
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelTimeLimit();
        timeLimitCancellation.Dispose();
        captureCancellation.Dispose();
        writer.Dispose();
    }

    private async Task CaptureAsync(GraphicsCaptureItem item, CancellationToken cancellationToken)
    {
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

            recordingTarget.OfferFrame(frame);
            writer.AddFrame(new Direct3DRecordingFrame(frame));
        }

        void OnClosed(GraphicsCaptureItem sender, object args) => writer.CompleteFrames();

        framePool.FrameArrived += OnFrameArrived;
        item.Closed += OnClosed;
        try
        {
            session.IsCursorCaptureEnabled = includeCursor;
            session.StartCapture();
            await writer.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (captureCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
            item.Closed -= OnClosed;
            writer.CompleteFrames();
        }
    }

    private async Task CompleteAsync()
    {
        Exception? failure = null;
        try
        {
            await Task.WhenAll(captureTask, writer.Completion).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (captureCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (Interlocked.CompareExchange(ref stopRequested, 1, 0) == 0)
        {
            try
            {
                await writer.StopAsync(discard: false, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                finalizationCompleted.TrySetResult();
            }
        }

        await finalizationCompleted.Task.ConfigureAwait(false);
        if (failure is not null)
        {
            throw failure;
        }
    }

    private async Task StopAtTimeLimitAsync()
    {
        try
        {
            await Task.Delay(timeLimit, timeLimitCancellation.Token).ConfigureAwait(false);
            await StopAsync(discard: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeLimitCancellation.IsCancellationRequested)
        {
        }
    }

    private void CancelTimeLimit()
    {
        try
        {
            timeLimitCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}