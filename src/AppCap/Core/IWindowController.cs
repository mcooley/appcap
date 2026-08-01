namespace AppCap;

public interface IWindowController
{
    Task BringToForegroundAsync(TargetWindow window, CancellationToken cancellationToken);

    Task<WindowBounds> GetBoundsAsync(TargetWindow window, CancellationToken cancellationToken);

    Task ResizeAsync(TargetWindow window, int width, int height, CancellationToken cancellationToken);
}