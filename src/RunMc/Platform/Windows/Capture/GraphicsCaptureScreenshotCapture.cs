using RunMc;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
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

    public async Task CapturePngAsync(MinecraftWindow window, string outputPath, bool includeCursor, CancellationToken cancellationToken)
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
            throw new RunMcException("Minecraft Bedrock window could not be captured.");
        }

        using Direct3DDeviceLease deviceLease = Direct3DDeviceFactory.CreateDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            deviceLease.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        using Direct3D11CaptureFrame frame = await CaptureFrameAsync(item, framePool, session, includeCursor, cancellationToken).ConfigureAwait(false);

        await SavePngAsync(frame, fullOutputPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SavePngAsync(Direct3D11CaptureFrame frame, string outputPath, CancellationToken cancellationToken)
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
            outputPath,
            FileAccessMode.ReadWrite,
            StorageOpenOptions.None,
            FileOpenDisposition.CreateAlways).AsTask(cancellationToken).ConfigureAwait(false);
        using SoftwareBitmap bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
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
            frameSource.TrySetException(new RunMcException("Minecraft Bedrock window was closed."));
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