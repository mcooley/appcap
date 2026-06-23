using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.SystemServices;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RunMc;

internal static class WindowsNative
{
    public const uint SwRestore = (uint)SHOW_WINDOW_CMD.SW_RESTORE;
    public const uint SwpNoSize = (uint)SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
    public const uint SwpNoMove = (uint)SET_WINDOW_POS_FLAGS.SWP_NOMOVE;
    public const uint SwpNoZOrder = (uint)SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
    public const uint SwpNoActivate = (uint)SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
    public const uint SwpShowWindow = (uint)SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;

    public const uint ProcessQueryLimitedInformation = (uint)PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION;

    public const uint WmMouseMove = PInvoke.WM_MOUSEMOVE;
    public const uint WmLButtonDown = PInvoke.WM_LBUTTONDOWN;
    public const uint WmLButtonUp = PInvoke.WM_LBUTTONUP;
    public const uint MkLButton = (uint)MODIFIERKEYS_FLAGS.MK_LBUTTON;

    public static unsafe nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId) =>
        (nint)PInvoke.OpenProcess((PROCESS_ACCESS_RIGHTS)desiredAccess, inheritHandle, processId).Value;

    public static bool CloseHandle(nint handle) => PInvoke.CloseHandle(new HANDLE(handle));

    public static uint GetCurrentThreadId() => PInvoke.GetCurrentThreadId();

    public static unsafe int GetPackageFamilyName(nint process, ref int packageFamilyNameLength, char[]? packageFamilyName)
    {
        uint length = checked((uint)packageFamilyNameLength);
        fixed (char* packageFamilyNamePointer = packageFamilyName)
        {
            WIN32_ERROR result = PInvoke.GetPackageFamilyName(new HANDLE(process), &length, new PWSTR(packageFamilyNamePointer));
            packageFamilyNameLength = checked((int)length);
            return checked((int)result);
        }
    }

    public static bool IsWindowVisible(nint windowHandle) => PInvoke.IsWindowVisible(new HWND(windowHandle));

    public static bool ShowWindow(nint windowHandle, uint command) => PInvoke.ShowWindow(new HWND(windowHandle), (SHOW_WINDOW_CMD)command);

    public static bool SetForegroundWindow(nint windowHandle) => PInvoke.SetForegroundWindow(new HWND(windowHandle));

    public static unsafe nint GetForegroundWindow() => (nint)PInvoke.GetForegroundWindow().Value;

    public static unsafe uint GetWindowThreadProcessId(nint windowHandle, out uint processId)
    {
        fixed (uint* processIdPointer = &processId)
        {
            return PInvoke.GetWindowThreadProcessId(new HWND(windowHandle), processIdPointer);
        }
    }

    public static bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach) => PInvoke.AttachThreadInput(idAttach, idAttachTo, attach);

    public static bool BringWindowToTop(nint windowHandle) => PInvoke.BringWindowToTop(new HWND(windowHandle));

    public static unsafe nint SetFocus(nint windowHandle) => (nint)PInvoke.SetFocus(new HWND(windowHandle)).Value;

    public static bool GetWindowRect(nint windowHandle, out RECT rect) => PInvoke.GetWindowRect(new HWND(windowHandle), out rect);

    public static bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int cx, int cy, uint flags) =>
        PInvoke.SetWindowPos(new HWND(windowHandle), new HWND(insertAfter), x, y, cx, cy, (SET_WINDOW_POS_FLAGS)flags);

    public static nint SendMessageW(nint windowHandle, uint message, nint wParam, nint lParam) =>
        PInvoke.SendMessage(new HWND(windowHandle), message, new WPARAM((nuint)wParam), new LPARAM(lParam)).Value;

    public static unsafe int DwmGetWindowAttribute(nint windowHandle, out RECT attributeValue, int attributeSize)
    {
        RECT rect = default;
        int result = PInvoke.DwmGetWindowAttribute(
            new HWND(windowHandle),
            DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            &rect,
            (uint)attributeSize).Value;
        attributeValue = rect;
        return result;
    }
}
