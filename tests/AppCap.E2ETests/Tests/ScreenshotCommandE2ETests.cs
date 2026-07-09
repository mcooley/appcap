namespace AppCap.E2ETests;

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
        Assert.Equal("Captured from AppCap E2E Test App 1.0.0.0", properties.Comments);
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

    [E2EFact]
    public async Task ScreenshotWhileRecordingReusesRecordingSession()
    {
        string recordingPath = Context.NewOutputPath("while-recording.mp4");
        string screenshotPath = Context.NewOutputPath("while-recording.png");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", recordingPath).AssertSuccess();
        await Task.Delay(500);

        // Taking a screenshot while a recording is running is served by the recording
        // worker's live capture session rather than starting a second one.
        Context.Run("screenshot", "--output", screenshotPath).AssertSuccess();

        Context.Run("record", "stop").AssertSuccess();
        await WaitForFileAsync(recordingPath);

        ImageInfo image = await E2EHelpers.ReadImageInfoAsync(screenshotPath);
        Assert.True(image.Width > 0 && image.Height > 0, "Expected a non-empty screenshot while recording.");

        ShellProperties properties = E2EHelpers.ReadShellProperties(screenshotPath);
        Assert.Equal("Captured from AppCap E2E Test App 1.0.0.0", properties.Comments);

        FileInfo recording = new(recordingPath);
        Assert.True(recording.Exists && recording.Length > 0, "Expected the recording to still be written after the screenshot.");
    }

    private static async Task WaitForFileAsync(string path)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            FileInfo file = new(path);
            if (file.Exists && file.Length > 0)
            {
                return;
            }

            await Task.Delay(250);
        }
    }
}