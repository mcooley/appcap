namespace RunMc;

public interface IInputInjector
{
    Task ClickAsync(MinecraftWindow window, int screenX, int screenY, CancellationToken cancellationToken);
}