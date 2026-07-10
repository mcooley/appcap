using AppCap.Protocol.Target;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;

namespace AppCap.Windows;

// Reads the raw BGRA8 pixels out of a captured Direct3D surface. This is the readback
// step that turns a GPU capture frame into the transport-friendly byte[] the capture
// protocol carries, so both the in-proc and recording hosts return identical raw image
// data and leave encoding to the client.
internal static class FramePixels
{
    public static async Task<CapturedFrame> ReadAsync(IDirect3DSurface surface, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        using SoftwareBitmap bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        global::Windows.Storage.Streams.Buffer buffer = new((uint)(width * height * 4));
        bitmap.CopyToBuffer(buffer);

        byte[] pixels = new byte[buffer.Length];
        using DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(pixels);

        return new CapturedFrame(width, height, pixels);
    }
}
