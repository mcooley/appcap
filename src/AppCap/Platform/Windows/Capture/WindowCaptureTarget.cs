using AppCap;
using AppCap.Protocol.Target;
using System.Runtime.InteropServices;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace AppCap.Windows;

// Target that captures a single frame from a window in-process, used when no recording
// is running for the target. It starts its own short-lived capture session, honours the
// cursor request fully (the captured cursor plus an overlay marker), and returns the raw
// frame pixels.
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

        string? capturedFrom = ScreenshotMetadata.TryCreate(window)?.CapturedFrom;

        using Direct3DDeviceLease deviceLease = Direct3DDeviceFactory.CreateDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            deviceLease.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        using Direct3D11CaptureFrame frame = await CaptureFrameAsync(item, framePool, session, includeCursor, cancellationToken).ConfigureAwait(false);

        if (includeCursor && TryGetCursorLocation(window, out float cursorX, out float cursorY))
        {
            using CursorRenderer cursorRenderer = new(cursorX, cursorY);
            IDirect3DSurface cursorSurface = cursorRenderer.Render(frame.Surface);
            try
            {
                return await FramePixels.ReadAsync(cursorSurface, capturedFrom, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                (cursorSurface as IDisposable)?.Dispose();
            }
        }

        return await FramePixels.ReadAsync(frame.Surface, capturedFrom, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryGetCursorLocation(TargetWindow window, out float x, out float y)
    {
        x = 0;
        y = 0;
        if (!PInvoke.GetCursorPos(out System.Drawing.Point cursorPosition))
        {
            return false;
        }

        int result = GetDwmExtendedFrameBounds(new HWND(window.Handle), out RECT bounds);
        if (result is not 0)
        {
            return false;
        }

        x = cursorPosition.X - bounds.left;
        y = cursorPosition.Y - bounds.top;
        return x >= 0 && y >= 0 && x < bounds.Width && y < bounds.Height;
    }

    private static unsafe int GetDwmExtendedFrameBounds(HWND windowHandle, out RECT rect)
    {
        rect = default;
        Span<byte> rectBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref rect, 1));
        int result = PInvoke.DwmGetWindowAttribute(
            windowHandle,
            global::Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            rectBytes).Value;
        return result;
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
