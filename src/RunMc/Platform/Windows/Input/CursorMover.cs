using RunMc;
using global::Windows.Win32;

namespace RunMc.Windows;

public sealed class CursorMover : ICursorMover
{
    public Task MoveToAsync(MinecraftWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PInvoke.SetCursorPos(screenX, screenY))
        {
            throw new RunMcException("Cursor could not be moved.");
        }

        return Task.CompletedTask;
    }
}