namespace RunMc.E2ETests;

public sealed class ScreenshotCommandE2ETests : E2ETestBase
{
    private static readonly PixelColor BackgroundColor = new(10, 90, 140);

    [E2EFact]
    public void ScreenshotWritesCapturedFromComment()
    {
        string path = Context.NewOutputPath("metadata.png");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("screenshot", "--output", path).AssertSuccess();

        ShellProperties properties = E2EHelpers.ReadShellProperties(path);
        Assert.True(string.IsNullOrEmpty(properties.Title));
        Assert.Equal("Captured from RunMc E2E Test App 1.0.0.0", properties.Comments);
    }

    [E2EFact]
    public async Task IncludeCursorControlsWhetherCursorAppearsInScreenshot()
    {
        const int cursorX = 500;
        const int cursorY = 220;
        E2EContext context = Context;
        string withoutCursorPath = context.NewOutputPath("without-cursor.png");
        string withCursorPath = context.NewOutputPath("with-cursor.png");

        context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        context.Run("hover", "-x", cursorX.ToString(System.Globalization.CultureInfo.InvariantCulture), "-y", cursorY.ToString(System.Globalization.CultureInfo.InvariantCulture)).AssertSuccess();
        context.Run("screenshot", "--output", withoutCursorPath).AssertSuccess();
        context.Run("screenshot", "--include-cursor", "--output", withCursorPath).AssertSuccess();

        PixelColor pixelWithoutCursor = await E2EHelpers.ReadPixelAsync(withoutCursorPath, cursorX, cursorY);
        PixelColor pixelWithCursor = await E2EHelpers.ReadPixelAsync(withCursorPath, cursorX, cursorY);

        PixelAssertions.AssertColorNear(BackgroundColor, pixelWithoutCursor);
        PixelAssertions.AssertColorNotNear(BackgroundColor, pixelWithCursor);
    }

    [E2EFact]
    public async Task CaptionControlsWhetherCaptionAppearsInScreenshot()
    {
        const int captionX = 266;
        const int captionY = 435;
        E2EContext context = Context;
        string withoutCaptionPath = context.NewOutputPath("without-caption.png");
        string withCaptionPath = context.NewOutputPath("with-caption.png");

        context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        context.Run("screenshot", "--output", withoutCaptionPath).AssertSuccess();
        context.Run("screenshot", "--caption", "E2E caption", "--output", withCaptionPath).AssertSuccess();

        PixelColor pixelWithoutCaption = await E2EHelpers.ReadPixelAsync(withoutCaptionPath, captionX, captionY);
        PixelColor pixelWithCaption = await E2EHelpers.ReadPixelAsync(withCaptionPath, captionX, captionY);

        PixelAssertions.AssertColorNear(BackgroundColor, pixelWithoutCaption);
        PixelAssertions.AssertColorNotNear(BackgroundColor, pixelWithCaption);
    }
}