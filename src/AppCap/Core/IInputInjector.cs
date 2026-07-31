namespace AppCap;

public interface IInputInjector
{
    Task TapAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken);

    Task MoveMouseAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken);

    Task ClickMouseAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken);
}