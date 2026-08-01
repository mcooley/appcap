using AppCap.Protocol.Target;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;

namespace AppCap.Windows;

internal sealed class AttachedCaptureSession : ITarget, IDisposable
{
    private readonly TargetWindow window;
    private readonly RecordingCaptureTarget captureTarget;
    private readonly CancellationTokenSource captureCancellation;
    private readonly object gate = new();
    private GraphicsCaptureSession? graphicsSession;
    private RecordingWriter? writer;
    private Task captureTask = Task.CompletedTask;
    private bool includeCursor = true;
    private bool disposed;

    public AttachedCaptureSession(TargetWindow window, CancellationToken cancellationToken)
    {
        this.window = window;
        captureTarget = new RecordingCaptureTarget(window);
        captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public Task Completion => captureTask;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public nint WindowHandle => window.Handle;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new AppCapException("Graphics capture is not supported on this Windows version.");
        }

        GraphicsCaptureItem item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new AppCapException("Target window could not be captured.");
        }

        Width = item.Size.Width;
        Height = item.Size.Height;
        captureTask = CaptureLoopAsync(item, captureCancellation.Token);
        await Task.Yield();
        if (captureTask.IsCompleted)
        {
            await captureTask.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (writer is null)
            {
                this.includeCursor = includeCursor;
                if (graphicsSession is not null)
                {
                    graphicsSession.IsCursorCaptureEnabled = includeCursor;
                }
            }
        }

        return captureTarget.CaptureFrameAsync(includeCursor, cancellationToken);
    }

    public void AttachWriter(RecordingWriter recordingWriter, bool includeCursor)
    {
        ArgumentNullException.ThrowIfNull(recordingWriter);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (writer is not null)
            {
                throw new AppCapException($"A recording is already running for target '{window.Application.Name}'.");
            }

            writer = recordingWriter;
            this.includeCursor = includeCursor;
            if (graphicsSession is not null)
            {
                graphicsSession.IsCursorCaptureEnabled = includeCursor;
            }
        }

        captureTarget.RequestFrame();
    }

    public void DetachWriter(RecordingWriter recordingWriter)
    {
        lock (gate)
        {
            if (ReferenceEquals(writer, recordingWriter))
            {
                writer = null;
            }
        }

        recordingWriter.CompleteFrames();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        captureCancellation.Cancel();
        try
        {
            captureTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (captureCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }

        captureCancellation.Dispose();
    }

    private async Task CaptureLoopAsync(GraphicsCaptureItem item, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await CaptureOnceAsync(item, cancellationToken).ConfigureAwait(false);
                if (!PInvoke.IsWindow(new HWND(window.Handle)))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);
                Width = item.Size.Width;
                Height = item.Size.Height;
            }
        }
        catch (OperationCanceledException) when (captureCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            RecordingWriter? activeWriter;
            lock (gate)
            {
                graphicsSession = null;
                activeWriter = writer;
                writer = null;
            }

            activeWriter?.CompleteFrames();
        }
    }

    private async Task CaptureOnceAsync(GraphicsCaptureItem item, CancellationToken cancellationToken)
    {
        using Direct3DDeviceLease deviceLease = Direct3DDeviceFactory.CreateDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            deviceLease.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        TaskCompletionSource captureCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int captureWidth = item.Size.Width;
        int captureHeight = item.Size.Height;

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            if (frame.ContentSize.Width != captureWidth || frame.ContentSize.Height != captureHeight)
            {
                captureWidth = frame.ContentSize.Width;
                captureHeight = frame.ContentSize.Height;
                Width = captureWidth;
                Height = captureHeight;
                frame.Dispose();
                if (captureWidth > 0 && captureHeight > 0)
                {
                    sender.Recreate(
                        deviceLease.Device,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        new global::Windows.Graphics.SizeInt32(captureWidth, captureHeight));
                }

                return;
            }

            captureTarget.OfferFrame(frame);
            RecordingWriter? activeWriter;
            lock (gate)
            {
                activeWriter = writer;
            }

            if (activeWriter is null)
            {
                frame.Dispose();
            }
            else
            {
                activeWriter.AddFrame(new Direct3DRecordingFrame(frame));
            }
        }

        void OnClosed(GraphicsCaptureItem sender, object args)
        {
            captureCompleted.TrySetResult();
        }

        using CancellationTokenSource windowMonitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task windowMonitor = MonitorTargetWindowAsync(captureCompleted, windowMonitorCancellation.Token);
        framePool.FrameArrived += OnFrameArrived;
        item.Closed += OnClosed;
        lock (gate)
        {
            graphicsSession = session;
            session.IsCursorCaptureEnabled = includeCursor;
        }

        try
        {
            session.StartCapture();
            await captureCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await windowMonitorCancellation.CancelAsync().ConfigureAwait(false);
            await windowMonitor.ConfigureAwait(false);
            framePool.FrameArrived -= OnFrameArrived;
            item.Closed -= OnClosed;
            lock (gate)
            {
                graphicsSession = null;
            }
        }
    }

    private async Task MonitorTargetWindowAsync(TaskCompletionSource captureCompleted, CancellationToken cancellationToken)
    {
        try
        {
            while (PInvoke.IsWindow(new HWND(window.Handle)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }

            captureCompleted.TrySetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
