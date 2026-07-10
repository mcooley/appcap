using AppCap.Protocol.Target;

namespace AppCap.Windows;

internal static class FrameCropper
{
    private const int BytesPerPixel = 4;

    public static CapturedFrame Crop(CapturedFrame frame, CropRectangle crop)
    {
        ArgumentNullException.ThrowIfNull(frame);
        crop.ValidateWithin(frame.Width, frame.Height);

        if (crop.X is 0 && crop.Y is 0 && crop.Width == frame.Width && crop.Height == frame.Height)
        {
            return frame;
        }

        byte[] croppedPixels = new byte[checked(crop.Width * crop.Height * BytesPerPixel)];
        int sourceStride = checked(frame.Width * BytesPerPixel);
        int destinationStride = checked(crop.Width * BytesPerPixel);
        int sourceOffset = checked(((crop.Y * frame.Width) + crop.X) * BytesPerPixel);

        for (int row = 0; row < crop.Height; row++)
        {
            Buffer.BlockCopy(
                frame.BgraPixels,
                sourceOffset + (row * sourceStride),
                croppedPixels,
                row * destinationStride,
                destinationStride);
        }

        return new CapturedFrame(crop.Width, crop.Height, croppedPixels);
    }
}
