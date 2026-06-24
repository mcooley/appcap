using Windows.Win32;

namespace RunMc;

public sealed class WindowsCursorMover : ICursorMover
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