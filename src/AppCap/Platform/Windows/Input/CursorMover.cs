using AppCap;
using global::Windows.Win32;

namespace AppCap.Windows;

public sealed class CursorMover : ICursorMover
{
    public Task MoveToAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PInvoke.SetCursorPos(screenX, screenY))
        {
            throw new AppCapException("Cursor could not be moved.");
        }

        return Task.CompletedTask;
    }
}