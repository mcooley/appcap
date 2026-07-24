namespace AppCap.E2ETests;

public abstract class E2ETestBase : IDisposable
{
    protected E2ETestBase()
    {
        E2EHelpers.CloseAppCapProcesses();
        E2EHelpers.CloseTestAppProcesses();
        Context = E2EContext.Current;
    }

    protected E2EContext Context { get; }

    protected void AttachInputDevices(params string[] devices)
    {
        foreach (string device in devices)
        {
            Context.Run("inputdevice", "attach", device).AssertSuccess();
        }
    }

    public void Dispose()
    {
        E2EHelpers.CloseAppCapProcesses();
        E2EHelpers.CloseTestAppProcesses();
        GC.SuppressFinalize(this);
    }
}