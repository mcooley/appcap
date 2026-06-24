namespace RunMc;

public interface ICursorMover
{
    Task MoveToAsync(MinecraftWindow window, int screenX, int screenY, CancellationToken cancellationToken);
}