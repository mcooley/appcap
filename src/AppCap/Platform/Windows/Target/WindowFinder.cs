using AppCap;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;

namespace AppCap.Windows;

public interface IWindowFinder
{
    TargetWindow? TryFindWindow(TargetApplication application);
}

public sealed class WindowFinder : IWindowFinder
{
    public TargetWindow? TryFindWindow(TargetApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        FindWindowState state = new(application.Id);
        GCHandle stateHandle = GCHandle.Alloc(state);
        try
        {
            unsafe
            {
                PInvoke.EnumWindows(&EnumWindowCallback, new LPARAM(GCHandle.ToIntPtr(stateHandle)));
            }
        }
        finally
        {
            stateHandle.Free();
        }

        return state.FoundWindow.IsNull ? null : new TargetWindow(application, state.FoundWindow);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL EnumWindowCallback(HWND windowHandle, LPARAM lParam)
    {
        if (GCHandle.FromIntPtr(lParam.Value).Target is not FindWindowState state)
        {
            return true;
        }

        if (!PInvoke.IsWindowVisible(windowHandle))
        {
            return true;
        }

        if (PInvoke.GetWindowThreadProcessId(windowHandle, out uint processId) == 0 || processId == 0)
        {
            return true;
        }

        if (ProcessPackage.TryGetApplicationUserModelId((int)processId, out string? processApplicationUserModelId) &&
            state.ApplicationId.Equals(processApplicationUserModelId, StringComparison.Ordinal))
        {
            state.FoundWindow = windowHandle;
            return false;
        }

        return true;
    }

    private sealed class FindWindowState(string applicationId)
    {
        public string ApplicationId { get; } = applicationId;

        public HWND FoundWindow { get; set; }
    }
}