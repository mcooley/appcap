namespace AppCap;

public interface IInputInjector
{
    Task ClickAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken);
}