namespace AppCap;

public interface IInputInjector
{
    Task TapAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken);
}