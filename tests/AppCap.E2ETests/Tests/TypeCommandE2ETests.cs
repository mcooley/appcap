namespace AppCap.E2ETests;

public sealed class TypeCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void TypeSucceedsWithAttachedKeyboardDevice()
    {
        AttachInputDevices("keyboard");
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("type", "abc").AssertSuccess();
    }
}