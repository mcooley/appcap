namespace AppCap.E2ETests;

public sealed class TapCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void TapUsesTouchDeviceAttachedWithTarget()
    {
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("tap", "-x", "150", "-y", "130").AssertSuccess();

        CommandResult devices = Context.Run("inputdevice", "list");
        devices.AssertSuccess();
        Assert.Contains("touch: attached", devices.StandardOutput, StringComparison.Ordinal);
    }
}