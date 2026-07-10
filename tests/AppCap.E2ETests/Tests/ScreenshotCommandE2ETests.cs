namespace AppCap.E2ETests;

public sealed class ScreenshotCommandE2ETests : E2ETestBase
{
    private static readonly PixelColor BackgroundColor = new(10, 90, 140);

    [E2EFact]
    public async Task ExcludeCursorWritesScreenshot()
    {
        const int cursorX = 500;
        const int cursorY = 220;
        E2EContext context = Context;
        string withoutCursorPath = context.NewOutputPath("without-cursor.png");
        string withCursorPath = context.NewOutputPath("with-cursor.png");

        context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        context.Run("hover", "-x", cursorX.ToString(System.Globalization.CultureInfo.InvariantCulture), "-y", cursorY.ToString(System.Globalization.CultureInfo.InvariantCulture)).AssertSuccess();
        context.Run("screenshot", "--exclude-cursor", "--output", withoutCursorPath).AssertSuccess();
        context.Run("screenshot", "--output", withCursorPath).AssertSuccess();

        ImageInfo imageWithoutCursor = await E2EHelpers.ReadImageInfoAsync(withoutCursorPath);
        ImageInfo imageWithCursor = await E2EHelpers.ReadImageInfoAsync(withCursorPath);

        Assert.True(imageWithoutCursor.Length > 0, "Expected an excluded-cursor screenshot.");
        Assert.True(imageWithCursor.Length > 0, "Expected a default-cursor screenshot.");
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
    public async Task CropSetsScreenshotDimensionsAndRendersCaptionAfterCrop()
    {
        const string crop = "160,200,320,240";
        const int captionX = 120;
        const int captionY = 188;
        E2EContext context = Context;
        string withoutCaptionPath = context.NewOutputPath("cropped-without-caption.png");
        string withCaptionPath = context.NewOutputPath("cropped-with-caption.png");

        context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        context.Run("screenshot", "--crop", crop, "--output", withoutCaptionPath).AssertSuccess();
        context.Run("screenshot", "--crop", crop, "--caption", "CROPPED", "--output", withCaptionPath).AssertSuccess();

        ImageInfo image = await E2EHelpers.ReadImageInfoAsync(withCaptionPath);
        ImagePixels withoutCaption = await E2EHelpers.ReadPixelsAsync(withoutCaptionPath);
        ImagePixels withCaption = await E2EHelpers.ReadPixelsAsync(withCaptionPath);

        Assert.Equal(320, image.Width);
        Assert.Equal(240, image.Height);
        PixelAssertions.AssertRegionNear(BackgroundColor, withoutCaption, captionX, captionY, 80, 24);
        PixelAssertions.AssertRegionContainsColorNotNear(BackgroundColor, withCaption, captionX, captionY, 80, 24);
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