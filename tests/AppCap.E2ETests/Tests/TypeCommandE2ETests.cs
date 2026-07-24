namespace AppCap.E2ETests;

public sealed class TypeCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void TypePreservesTextAndBracketedKeys()
    {
        AttachInputDevices("keyboard");
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("type", "[End][Enter]AppCap test").AssertSuccess();

        string title = E2EHelpers.WaitForTestAppWindowTitle(TimeSpan.FromSeconds(5));
        Assert.Equal("AppCap E2E Test App | typed:\\rAppCap test", title);
    }
}