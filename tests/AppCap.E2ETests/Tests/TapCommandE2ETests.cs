namespace AppCap.E2ETests;

public sealed class TapCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void TapSucceedsWithAttachedTouchDevice()
    {
        AttachInputDevices("touch");
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("tap", "-x", "150", "-y", "130").AssertSuccess();
    }
}