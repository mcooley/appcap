using AppCap;
using System.Diagnostics;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;

namespace AppCap.Windows;

public interface IWindowFinder
{
    TargetWindow? TryFindWindow(TargetConfiguration target, AppCapTargetConfig application);
}

public sealed class WindowFinder : IWindowFinder
{
    public TargetWindow? TryFindWindow(TargetConfiguration target, AppCapTargetConfig application)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(application);

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (!ProcessPackage.TryGetPackageFamilyName(process.Id, out string? processPackageFamilyName) ||
                    !application.PackageFamilyName.Equals(processPackageFamilyName, StringComparison.Ordinal))
                {
                    continue;
                }

                nint windowHandle = process.MainWindowHandle;
                if (windowHandle != 0 && PInvoke.IsWindowVisible(new HWND(windowHandle)))
                {
                    return new TargetWindow(target, application, windowHandle);
                }
            }
        }

        return null;
    }
}