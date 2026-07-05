using AppCap;
using System.Diagnostics;

namespace AppCap.Windows;

public interface ITargetLauncher
{
    void Launch(AppCapTargetConfig target);
}

public sealed class TargetLauncher : ITargetLauncher
{
    public void Launch(AppCapTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Id);

        ProcessStartInfo startInfo = new()
        {
            FileName = "shell:AppsFolder\\" + target.Id,
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
