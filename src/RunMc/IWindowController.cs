namespace RunMc;

public interface IWindowController
{
    Task BringToForegroundAsync(MinecraftWindow window, CancellationToken cancellationToken);

    Task<WindowBounds> GetBoundsAsync(MinecraftWindow window, CancellationToken cancellationToken);

    Task ResizeAsync(MinecraftWindow window, int width, int height, CancellationToken cancellationToken);
}