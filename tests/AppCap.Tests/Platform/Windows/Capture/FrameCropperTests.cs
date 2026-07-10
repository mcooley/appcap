using AppCap.Protocol.Target;
using AppCap.Windows;

namespace AppCap.Tests;

public sealed class FrameCropperTests
{
    [Fact]
    public void CropReturnsRequestedPixelsInRowMajorOrder()
    {
        CapturedFrame frame = new(4, 2,
        [
            1, 2, 3, 4,   5, 6, 7, 8,   9, 10, 11, 12,   13, 14, 15, 16,
            17, 18, 19, 20,   21, 22, 23, 24,   25, 26, 27, 28,   29, 30, 31, 32,
        ]);

        CapturedFrame cropped = FrameCropper.Crop(frame, new CropRectangle(1, 0, 2, 2));

        Assert.Equal(2, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(
        [
            5, 6, 7, 8,   9, 10, 11, 12,
            21, 22, 23, 24,   25, 26, 27, 28,
        ],
        cropped.BgraPixels);
    }
}
