namespace AppCap.Tests;

public sealed class CropRectangleTests
{
    [Fact]
    public void TryParseAcceptsInvariantCropSyntax()
    {
        Assert.True(CropRectangle.TryParse("10,20,300,200", out CropRectangle crop));
        Assert.Equal(new CropRectangle(10, 20, 300, 200), crop);
    }

    [Fact]
    public void ValidateWithinRejectsCropOutsideFrame()
    {
        CropRectangle crop = new(500, 0, 200, 100);

        AppCapException exception = Assert.Throws<AppCapException>(() => crop.ValidateWithin(640, 480));

        Assert.Contains("640x480", exception.Message, StringComparison.Ordinal);
        Assert.Contains("x + width <= 640", exception.Message, StringComparison.Ordinal);
    }
}
