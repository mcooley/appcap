using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace RunMc;

public sealed class WindowsGraphicsCaptureScreenshotCapture : IScreenshotCapture
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    public async Task CapturePngAsync(MinecraftWindow window, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new RunMcException("Screenshot capture is not supported on this Windows version.");
        }

        _ = WindowsNative.ShowWindow(window.Handle, WindowsNative.SwRestore);

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

        using CanvasDevice device = CanvasDevice.GetSharedDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        using Direct3D11CaptureFrame frame = await CaptureFrameAsync(item, framePool, session, cancellationToken).ConfigureAwait(false);
        using CanvasBitmap bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);

        await bitmap.SaveAsync(fullOutputPath, CanvasBitmapFileFormat.Png).AsTask(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Direct3D11CaptureFrame> CaptureFrameAsync(
        GraphicsCaptureItem item,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
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
            session.IsCursorCaptureEnabled = false;
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