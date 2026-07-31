namespace AppCap.E2ETests;

public sealed class TargetSessionCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void ListAndDetachReflectSessionState()
    {
        CommandResult attached = Context.RunUnscoped("target", "list");
        attached.AssertSuccess();
        Assert.Contains($"{Context.Target}: attached, running", attached.StandardOutput, StringComparison.Ordinal);

        Context.RunUnscoped("target", "detach").AssertSuccess();

        CommandResult detached = Context.RunUnscoped("target", "list");
        detached.AssertSuccess();
        Assert.Contains($"{Context.Target}: detached, running", detached.StandardOutput, StringComparison.Ordinal);

        CommandResult command = Context.Run("screenshot", "--output", Context.NewOutputPath("detached.png"));
        Assert.NotEqual(0, command.ExitCode);
        Assert.Contains("not attached", command.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}