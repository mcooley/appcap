namespace AppCap.E2ETests;

public sealed class TargetSessionCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void LaunchStartsAttachedStoppedTarget()
    {
        Context.RunUnscoped("target", "detach").AssertSuccess();
        E2EHelpers.CloseTestAppProcesses();

        Context.RunUnscoped("target", "attach", Context.Target, "--no-launch").AssertSuccess();
        CommandResult stopped = Context.RunUnscoped("target", "list");
        stopped.AssertSuccess();
        Assert.Contains($"{Context.Target}: attached, stopped", stopped.StandardOutput, StringComparison.Ordinal);

        Context.RunUnscoped("target", "launch").AssertSuccess();
        CommandResult running = Context.RunUnscoped("target", "list");
        running.AssertSuccess();
        Assert.Contains($"{Context.Target}: attached, running", running.StandardOutput, StringComparison.Ordinal);
    }

    [E2EFact]
    public void ListAndDetachReflectSessionState()
    {
        CommandResult attached = Context.RunUnscoped("target", "list");
        attached.AssertSuccess();
        Assert.Contains($"{Context.Target}: attached, running", attached.StandardOutput, StringComparison.Ordinal);

        CommandResult launch = Context.RunUnscoped("target", "launch", Context.Target);
        Assert.NotEqual(0, launch.ExitCode);
        Assert.Contains($"Target '{Context.Target}' is already running.", launch.StandardError, StringComparison.Ordinal);

        Context.RunUnscoped("target", "detach").AssertSuccess();

        CommandResult detached = Context.RunUnscoped("target", "list");
        detached.AssertSuccess();
        Assert.Contains($"{Context.Target}: detached, running", detached.StandardOutput, StringComparison.Ordinal);

        CommandResult command = Context.Run("screenshot", "--output", Context.NewOutputPath("detached.png"));
        Assert.NotEqual(0, command.ExitCode);
        Assert.Contains("not attached", command.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}