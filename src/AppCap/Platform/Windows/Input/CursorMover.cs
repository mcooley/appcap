using AppCap;
using System.Drawing;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;

namespace AppCap.Windows;

public sealed class CursorMover : ICursorMover
{
    private const uint WmMouseMove = 0x0200;

    public Task MoveToAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        if (PInvoke.SetCursorPos(screenX, screenY))
        {
            return Task.CompletedTask;
        }

        HWND hwnd = new(window.Handle);
        Point clientPoint = new(screenX, screenY);
        if (!PInvoke.ScreenToClient(hwnd, ref clientPoint))
        {
            throw new AppCapException("Cursor could not be moved.");
        }

        _ = PInvoke.SendMessage(hwnd, WmMouseMove, new WPARAM(0), ToMouseLParam(clientPoint));
        return Task.CompletedTask;
    }

    private static LPARAM ToMouseLParam(Point clientPoint) =>
        new((clientPoint.X & 0xffff) | ((clientPoint.Y & 0xffff) << 16));
}