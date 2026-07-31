namespace AppCap.E2ETests;

public sealed class InputDeviceCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void ListReportsWindowsInputDevices()
    {
        AttachInputDevices("touch", "keyboard");
        CommandResult result = Context.Run("inputdevice", "list");

        result.AssertSuccess();
        Assert.Contains("touch: attached", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("keyboard: attached", result.StandardOutput, StringComparison.Ordinal);
    }

    [E2EFact]
    public void TapReattachesRemovedTouchDevice()
    {
        AttachInputDevices("touch");
        Context.Run("inputdevice", "remove", "touch").AssertSuccess();

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("tap", "--device", "touch", "-x", "150", "-y", "130").AssertSuccess();
        Assert.Contains("touch: attached", Context.Run("inputdevice", "list").StandardOutput, StringComparison.Ordinal);
    }

    [E2EFact]
    public void UnsupportedAndDuplicateDevicesFail()
    {
        AttachInputDevices("touch");
        CommandResult duplicate = Context.Run("inputdevice", "attach", "touch");
        Assert.NotEqual(0, duplicate.ExitCode);
        Assert.Contains("already attached", duplicate.StandardError, StringComparison.OrdinalIgnoreCase);

        CommandResult unsupported = Context.Run("inputdevice", "attach", "mouse");
        Assert.NotEqual(0, unsupported.ExitCode);
        Assert.Contains("not supported", unsupported.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}
