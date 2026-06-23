namespace RunMc;

public interface IInputInjector
{
    Task ClickAsync(MinecraftWindow window, int x, int y, CancellationToken cancellationToken);
}