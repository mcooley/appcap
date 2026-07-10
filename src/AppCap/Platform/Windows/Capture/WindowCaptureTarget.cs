using AppCap;
using AppCap.Protocol.Target;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace AppCap.Windows;

// Target that captures a single frame from a window in-process, used when no recording
// is running for the target. It starts its own short-lived capture session, honours the
// cursor request through the system compositor, and returns the raw frame pixels.
internal sealed class WindowCaptureTarget : ITarget
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    private readonly TargetWindow window;

    public WindowCaptureTarget(TargetWindow window) => this.window = window;

    public async Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new AppCapException("Screenshot capture is not supported on this Windows version.");
        }

        _ = PInvoke.ShowWindow(new HWND(window.Handle), SHOW_WINDOW_CMD.SW_RESTORE);

        GraphicsCaptureItem item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new AppCapException("Target window could not be captured.");
        }

        using Direct3DDeviceLease deviceLease = Direct3DDeviceFactory.CreateDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            deviceLease.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        using Direct3D11CaptureFrame frame = await CaptureFrameAsync(item, framePool, session, includeCursor, cancellationToken).ConfigureAwait(false);

        return await FramePixels.ReadAsync(frame.Surface, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Direct3D11CaptureFrame> CaptureFrameAsync(
        GraphicsCaptureItem item,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
        bool includeCursor,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<Direct3D11CaptureFrame> frameSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = framePool.TryGetNextFrame();
            if (frame is not null)
            {
                frameSource.TrySetResult(frame);
            }
        }

        void OnClosed(GraphicsCaptureItem sender, object args)
        {
            frameSource.TrySetException(new AppCapException("Target window was closed."));
        }

        framePool.FrameArrived += OnFrameArrived;
        item.Closed += OnClosed;
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CaptureTimeout);
        using CancellationTokenRegistration registration = timeoutSource.Token.Register(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                frameSource.TrySetCanceled(cancellationToken);
            }
            else
            {
                frameSource.TrySetException(new AppCapException("Screenshot capture timed out."));
            }
        });

        try
        {
            session.IsCursorCaptureEnabled = includeCursor;
            session.StartCapture();
            return await frameSource.Task.ConfigureAwait(false);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
            item.Closed -= OnClosed;
        }
    }
}
