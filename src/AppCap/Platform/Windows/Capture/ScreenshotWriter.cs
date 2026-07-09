using AppCap.Protocol.Target;
using global::Windows.Foundation;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage;
using global::Windows.Storage.Streams;

namespace AppCap.Windows;

// Worker-side half of a screenshot: turns the raw image data produced by a target into a
// PNG file. Rendering a caption and writing the file are the worker's responsibility, so
// this reuses the D2D caption renderer (by rebuilding a surface from the raw pixels) and
// writes the target-provided "captured from" text as the PNG comment.
internal static class ScreenshotWriter
{
    public static async Task WriteAsync(CapturedFrame frame, string outputPath, string? caption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            await WriteWithCaptionAsync(frame, fullOutputPath, caption, cancellationToken).ConfigureAwait(false);
            return;
        }

        using SoftwareBitmap bitmap = CreateBitmap(frame);
        await SavePngAsync(bitmap, fullOutputPath, frame.CapturedFrom, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteWithCaptionAsync(CapturedFrame frame, string outputPath, string caption, CancellationToken cancellationToken)
    {
        using Direct3DImageSurface sourceSurface = Direct3DSurfaceFactory.CreateFromBgraPixels(frame.Width, frame.Height, frame.BgraPixels);
        using CaptionRenderer captionRenderer = new((uint)frame.Width, (uint)frame.Height, caption);
        IDirect3DSurface captionedSurface = captionRenderer.Render(sourceSurface.Surface);
        try
        {
            using SoftwareBitmap bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captionedSurface, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
            await SavePngAsync(bitmap, outputPath, frame.CapturedFrom, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (captionedSurface as IDisposable)?.Dispose();
        }
    }

    private static SoftwareBitmap CreateBitmap(CapturedFrame frame)
    {
        DataWriter writer = new();
        writer.WriteBytes(frame.BgraPixels);
        IBuffer buffer = writer.DetachBuffer();
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Premultiplied);
    }

    private static async Task SavePngAsync(SoftwareBitmap bitmap, string outputPath, string? capturedFrom, CancellationToken cancellationToken)
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
            outputPath,
            FileAccessMode.ReadWrite,
            StorageOpenOptions.None,
            FileOpenDisposition.CreateAlways).AsTask(cancellationToken).ConfigureAwait(false);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        if (!string.IsNullOrWhiteSpace(capturedFrom))
        {
            BitmapPropertySet properties = new()
            {
                ["System.Comment"] = new BitmapTypedValue(capturedFrom, PropertyType.String),
            };
            await encoder.BitmapProperties.SetPropertiesAsync(properties).AsTask(cancellationToken).ConfigureAwait(false);
        }

        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }
}
