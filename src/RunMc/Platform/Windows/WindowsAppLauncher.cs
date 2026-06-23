using System.Diagnostics;

namespace RunMc;

public interface IWindowsAppLauncher
{
    void LaunchAumid(string aumid);
}

public sealed class WindowsAppLauncher : IWindowsAppLauncher
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
            throw new RunMcException("Minecraft Bedrock could not be launched.", exception);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new RunMcException("Minecraft Bedrock could not be launched.", exception);
        }
    }
}