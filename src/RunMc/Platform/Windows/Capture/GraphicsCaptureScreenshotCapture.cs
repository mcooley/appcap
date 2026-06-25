using RunMc;
using System.Runtime.InteropServices;

using global::Windows.Foundation;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage;
using global::Windows.Storage.Streams;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace RunMc.Windows;

public sealed class GraphicsCaptureScreenshotCapture : IScreenshotCapture
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    public async Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new RunMcException("Screenshot capture is not supported on this Windows version.");
        }

        _ = PInvoke.ShowWindow(new HWND(window.Handle), SHOW_WINDOW_CMD.SW_RESTORE);

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        GraphicsCaptureItem item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new RunMcException("Target window could not be captured.");
        }

        ScreenshotMetadata? metadata = ScreenshotMetadata.TryCreate(window);

        using Direct3DDeviceLease deviceLease = Direct3DDeviceFactory.CreateDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            deviceLease.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        using Direct3D11CaptureFrame frame = await CaptureFrameAsync(item, framePool, session, includeCursor, cancellationToken).ConfigureAwait(false);

        IDirect3DSurface surface = frame.Surface;
        List<IDisposable> renderedSurfaceLeases = [];
        if (!string.IsNullOrWhiteSpace(caption))
        {
            using CaptionRenderer captionRenderer = new((uint)frame.ContentSize.Width, (uint)frame.ContentSize.Height, caption);
            surface = captionRenderer.Render(frame.Surface);
            if (surface is IDisposable captionedSurfaceLease)
            {
                renderedSurfaceLeases.Add(captionedSurfaceLease);
            }
        }

        if (includeCursor && TryGetCursorLocation(window, out float cursorX, out float cursorY))
        {
            using CursorRenderer cursorRenderer = new(cursorX, cursorY);
            surface = cursorRenderer.Render(surface);
            if (surface is IDisposable cursorSurfaceLease)
            {
                renderedSurfaceLeases.Add(cursorSurfaceLease);
            }
        }

        try
        {
            await SavePngAsync(surface, fullOutputPath, metadata, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (IDisposable lease in renderedSurfaceLeases)
            {
                lease.Dispose();
            }
        }
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

    private static async Task SavePngAsync(IDirect3DSurface surface, string outputPath, ScreenshotMetadata? metadata, CancellationToken cancellationToken)
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
            outputPath,
            FileAccessMode.ReadWrite,
            StorageOpenOptions.None,
            FileOpenDisposition.CreateAlways).AsTask(cancellationToken).ConfigureAwait(false);
        using SoftwareBitmap bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        if (metadata is not null)
        {
            await SetMetadataAsync(encoder, metadata, cancellationToken).ConfigureAwait(false);
        }
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetMetadataAsync(BitmapEncoder encoder, ScreenshotMetadata metadata, CancellationToken cancellationToken)
    {
        BitmapPropertySet properties = new()
        {
            ["System.Comment"] = new BitmapTypedValue(metadata.CapturedFrom, PropertyType.String),
        };
        await encoder.BitmapProperties.SetPropertiesAsync(properties).AsTask(cancellationToken).ConfigureAwait(false);
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
            frameSource.TrySetException(new RunMcException("Target window was closed."));
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
                frameSource.TrySetException(new RunMcException("Screenshot capture timed out."));
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