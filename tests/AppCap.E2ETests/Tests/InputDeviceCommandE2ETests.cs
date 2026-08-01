namespace AppCap.E2ETests;

public sealed class InputDeviceCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void ListReportsWindowsInputDevices()
    {
        CommandResult result = Context.Run("inputdevice", "list");

        result.AssertSuccess();
        Assert.Contains("touch: attached", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("keyboard: attached", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("mouse: attached", result.StandardOutput, StringComparison.Ordinal);
    }

    [E2EFact]
    public void TapDoesNotReattachRemovedTouchDevice()
    {
        Context.Run("inputdevice", "remove", "touch").AssertSuccess();

        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        CommandResult tap = Context.Run("tap", "--device", "touch", "-x", "150", "-y", "130");

        Assert.NotEqual(0, tap.ExitCode);
        Assert.Contains("not attached", tap.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("touch: detached", Context.Run("inputdevice", "list").StandardOutput, StringComparison.Ordinal);
    }

    [E2EFact]
    public void UnsupportedAndDuplicateDevicesFail()
    {
        CommandResult duplicate = Context.Run("inputdevice", "attach", "touch");
        Assert.NotEqual(0, duplicate.ExitCode);
        Assert.Contains("already attached", duplicate.StandardError, StringComparison.OrdinalIgnoreCase);

        CommandResult unsupported = Context.Run("inputdevice", "attach", "gamepad");
        Assert.NotEqual(0, unsupported.ExitCode);
        Assert.Contains("not supported", unsupported.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [E2EFact]
    public void MouseCommandsDoNotReattachRemovedMouseDevice()
    {
        Context.Run("inputdevice", "remove", "mouse").AssertSuccess();

        CommandResult move = Context.Run("mouseto", "150,130");
        CommandResult click = Context.Run("click", "150,130");

        Assert.NotEqual(0, move.ExitCode);
        Assert.Contains("attached", move.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(0, click.ExitCode);
        Assert.Contains("attached", click.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mouse: detached", Context.Run("inputdevice", "list").StandardOutput, StringComparison.Ordinal);
    }
}
