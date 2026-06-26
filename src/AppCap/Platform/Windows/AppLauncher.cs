using AppCap;
using System.Diagnostics;

namespace AppCap.Windows;

public interface IAppLauncher
{
    void LaunchAumid(string aumid);
}

public sealed class AppLauncher : IAppLauncher
{
    public void LaunchAumid(string aumid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aumid);

        ProcessStartInfo startInfo = new()
        {
            FileName = "shell:AppsFolder\\" + aumid,
            UseShellExecute = true,
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (InvalidOperationException exception)
        {
            throw new AppCapException("Target application could not be launched.", exception);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new AppCapException("Target application could not be launched.", exception);
        }
    }
}