namespace RunMc;

public interface ICursorMover
{
    Task MoveToAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken);
}