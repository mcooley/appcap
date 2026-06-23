using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemServices;

namespace RunMc;

public sealed class WindowMessageInputInjector : IInputInjector
{
    public Task ClickAsync(MinecraftWindow window, int x, int y, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        nint coordinates = MakeLParam(x, y);
        HWND hwnd = new(window.Handle);
        _ = PInvoke.SendMessage(hwnd, PInvoke.WM_MOUSEMOVE, 0, new LPARAM(coordinates));
        _ = PInvoke.SendMessage(hwnd, PInvoke.WM_LBUTTONDOWN, new WPARAM((nuint)MODIFIERKEYS_FLAGS.MK_LBUTTON), new LPARAM(coordinates));
        _ = PInvoke.SendMessage(hwnd, PInvoke.WM_LBUTTONUP, 0, new LPARAM(coordinates));

        return Task.CompletedTask;
    }

    private static nint MakeLParam(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));
}