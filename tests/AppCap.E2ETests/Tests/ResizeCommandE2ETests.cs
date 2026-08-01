namespace AppCap.E2ETests;

public sealed class ResizeCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public async Task ResizeSetsScreenshotDimensions()
    {
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        string path = Context.NewOutputPath("resize-screenshot.png");
        Context.Run("screenshot", "--output", path).AssertSuccess();

        ImageInfo image = await E2EHelpers.ReadImageInfoAsync(path);
        Assert.Equal(640, image.Width);
        Assert.Equal(480, image.Height);
        Assert.True(image.Length > 0);
    }
}