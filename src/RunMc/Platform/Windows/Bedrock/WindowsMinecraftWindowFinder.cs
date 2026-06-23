using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace RunMc;

public interface IWindowsMinecraftWindowFinder
{
    MinecraftWindow? TryFindWindow(string packageFamilyName, TargetKind target);
}

public sealed class WindowsMinecraftWindowFinder : IWindowsMinecraftWindowFinder
{
    public MinecraftWindow? TryFindWindow(string packageFamilyName, TargetKind target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFamilyName);

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (!WindowsProcessPackage.TryGetPackageFamilyName(process.Id, out string? processPackageFamilyName) ||
                    !packageFamilyName.Equals(processPackageFamilyName, StringComparison.Ordinal))
                {
                    continue;
                }

                nint windowHandle = process.MainWindowHandle;
                if (windowHandle != 0 && PInvoke.IsWindowVisible(new HWND(windowHandle)))
                {
                    return new MinecraftWindow(target, windowHandle);
                }
            }
        }

        return null;
    }
}