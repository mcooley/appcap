namespace RunMc.E2ETests;

public sealed class RecordCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void RecordStopBeforeStartFails()
    {
        CommandResult result = Context.Run("record", "stop");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No recording is running", result.StandardError, StringComparison.Ordinal);
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

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        Context.Run("hover", "-x", "330", "-y", "130").AssertSuccess();
        Context.Run("click", "-x", "150", "-y", "130").AssertSuccess();
        await Task.Delay(500);
        Context.Run("record", "stop").AssertSuccess();
        await WaitForMp4FileAsync(path);

        AssertMp4FileWasWritten(path);
    }

    [E2EFact]
    public async Task ClosingWindowWhileRecordingWritesMp4File()
    {
        string path = Context.NewOutputPath("closed-window.mp4");

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("record", "start", "--output", path).AssertSuccess();
        await Task.Delay(500);
        Context.Run("hover", "-x", "330", "-y", "130").AssertSuccess();
        Context.Run("click", "-x", "150", "-y", "130").AssertSuccess();
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