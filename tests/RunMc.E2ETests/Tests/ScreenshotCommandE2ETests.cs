namespace RunMc.E2ETests;

public sealed class ScreenshotCommandE2ETests : E2ETestBase
{
    private static readonly PixelColor BackgroundColor = new(10, 90, 140);

    [E2EFact]
    public void ScreenshotWritesCapturedFromMetadataToCommentsOnly()
    {
        string path = Context.NewOutputPath("metadata.png");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("screenshot", "--output", path).AssertSuccess();

        ShellProperties properties = E2EHelpers.ReadShellProperties(path);
        Assert.True(string.IsNullOrEmpty(properties.Title));
        Assert.StartsWith("Captured from ", properties.Comments, StringComparison.Ordinal);
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
}