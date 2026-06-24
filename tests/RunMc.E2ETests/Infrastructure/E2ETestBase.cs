namespace RunMc.E2ETests;

public abstract class E2ETestBase : IDisposable
{
    protected E2ETestBase()
    {
        E2EHelpers.CloseTestAppProcesses();
        Context = E2EContext.Current;
    }

    protected E2EContext Context { get; }

    public void Dispose()
    {
        E2EHelpers.CloseTestAppProcesses();
        GC.SuppressFinalize(this);
    }
}