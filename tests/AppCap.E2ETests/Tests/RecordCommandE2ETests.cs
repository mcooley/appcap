namespace AppCap.E2ETests;

public sealed class RecordCommandE2ETests : E2ETestBase
{
    private static readonly PixelColor BackgroundColor = new(10, 90, 140);

    [E2EFact]
    public void RecordStopBeforeStartFails()
    {
        CommandResult result = Context.Run("record", "stop");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No recording is running", result.StandardError, StringComparison.Ordinal);
    }

    [E2EFact]
    public void RecordCancelBeforeStartFails()
    {
        CommandResult result = Context.Run("record", "cancel");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No recording is running", result.StandardError, StringComparison.Ordinal);
    }

    [E2EFact]
    public async Task RecordCancelDiscardsOutputFile()
    {
        string path = Context.NewOutputPath("cancelled.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "cancel").AssertSuccess();
        await Task.Delay(500);

        Assert.False(File.Exists(path), $"Expected no output file at '{path}' after cancelling the recording.");
    }

    [E2EFact]
    public async Task RecordStartFailsWhenRecordingIsAlreadyRunning()
    {
        string path = Context.NewOutputPath("already-running.mp4");
        string secondPath = Context.NewOutputPath("already-running-second.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        CommandResult result = Context.Run("record", "start", "--output", secondPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("A recording is already running", result.StandardError, StringComparison.Ordinal);
        Context.Run("record", "stop").AssertSuccess();
    }

    [E2EFact]
    public async Task RecordStartChangeAndStopWritesMp4File()
    {
        string path = Context.NewOutputPath("recording.mp4");

        AttachInputDevices("touch");
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        Context.Run("tap", "-x", "150", "-y", "130").AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(path);

        AssertMp4FileWasWritten(path);
    }

    [E2EFact]
    public async Task RecordCaptionsAppearThenFadeFromRecording()
    {
        const int captionX = 266;
        const int captionY = 435;
        string path = Context.NewOutputPath("captions.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        Context.Run("record", "caption", "First caption").AssertSuccess();
        await Task.Delay(750);
        Context.Run("record", "caption", "E2E caption").AssertSuccess();
        await Task.Delay(TimeSpan.FromSeconds(6));
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(path);

        PixelColor captioned = await E2EHelpers.ReadVideoPixelAsync(path, TimeSpan.FromSeconds(1.5), captionX, captionY);
        TimeSpan duration = await E2EHelpers.ReadVideoDurationAsync(path);
        PixelColor faded = await E2EHelpers.ReadVideoPixelAsync(path, duration - TimeSpan.FromMilliseconds(100), captionX, captionY);

        PixelAssertions.AssertColorNotNear(BackgroundColor, captioned);
        PixelAssertions.AssertColorNear(BackgroundColor, faded);
    }

    [E2EFact]
    public async Task ExcludeCursorWritesRecording()
    {
        string withCursorPath = Context.NewOutputPath("with-cursor.mp4");
        string withoutCursorPath = Context.NewOutputPath("without-cursor.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", withCursorPath).AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(withCursorPath);

        Context.Run("record", "start", "--output", withoutCursorPath, "--exclude-cursor").AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(withoutCursorPath);

        AssertMp4FileWasWritten(withCursorPath);
        AssertMp4FileWasWritten(withoutCursorPath);
    }

    [E2EFact]
    public async Task CropSetsRecordingDimensionsAndRendersCaptionAfterCrop()
    {
        const string crop = "160,200,320,240";
        const int captionX = 120;
        const int captionY = 188;
        string path = Context.NewOutputPath("cropped-recording.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path, "--crop", crop).AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "caption", "CROPPED").AssertSuccess();
        await Task.Delay(1000);
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(path);

        VideoInfo video = await E2EHelpers.ReadVideoInfoAsync(path);
        ImagePixels captioned = await E2EHelpers.ReadVideoPixelsAsync(path, TimeSpan.FromSeconds(1));

        Assert.Equal(320, video.Width);
        Assert.Equal(240, video.Height);
        PixelAssertions.AssertRegionContainsColorNotNear(BackgroundColor, captioned, captionX, captionY, 80, 24);
    }

    [E2EFact]
    public async Task OddWindowDimensionsAreTrimmedForMp4Encoding()
    {
        string path = Context.NewOutputPath("odd-dimensions.mp4");

        Context.Run("resize", "--width", "641", "--height", "481").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(path);

        VideoInfo video = await E2EHelpers.ReadVideoInfoAsync(path);

        Assert.Equal(640, video.Width);
        Assert.Equal(480, video.Height);
        AssertMp4FileWasWritten(path);
    }

    [E2EFact]
    public async Task RecordTimeLimitSavesMp4File()
    {
        string path = Context.NewOutputPath("time-limited.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path, "--time-limit", "0.05").AssertSuccess();
        await Task.Delay(TimeSpan.FromSeconds(10));

        CommandResult stopResult = Context.Run("record", "stop");
        Assert.NotEqual(0, stopResult.ExitCode);
        Assert.Contains("No recording is running", stopResult.StandardError, StringComparison.Ordinal);

        await WaitForMp4FileAsync(path);
        AssertMp4FileWasWritten(path);
    }

    [E2EFact]
    public async Task ClosingWindowWhileRecordingWritesMp4File()
    {
        string path = Context.NewOutputPath("closed-window.mp4");

        AttachInputDevices("touch");
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        Context.Run("tap", "-x", "150", "-y", "130").AssertSuccess();
        await Task.Delay(500);

        E2EHelpers.CloseTestAppProcesses();
        await WaitForMp4FileAsync(path);

        AssertMp4FileWasWritten(path);
    }

    private static void AssertMp4FileWasWritten(string path)
    {
        FileInfo file = new(path);
        Assert.True(file.Exists, $"Expected MP4 file to exist at '{path}'.");
        Assert.True(file.Length > 0, $"Expected MP4 file at '{path}' to be non-empty.");
    }

    private static async Task WaitForMp4FileAsync(string path)
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