namespace AppCap.E2ETests;

public abstract class E2ETestBase : IDisposable
{
    protected E2ETestBase()
    {
        E2EHelpers.CloseAppCapProcesses();
        E2EHelpers.CloseTestAppProcesses();
        Context = E2EContext.Current;
        Context.RunUnscoped("target", "attach", Context.Target).AssertSuccess();
    }

    protected E2EContext Context { get; }

    public void Dispose()
    {
        E2EHelpers.CloseAppCapProcesses();
        E2EHelpers.CloseTestAppProcesses();
        GC.SuppressFinalize(this);
    }
}