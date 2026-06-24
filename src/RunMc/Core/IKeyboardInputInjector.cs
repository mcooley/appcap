namespace RunMc;

public interface IKeyboardInputInjector
{
    Task TypeAsync(MinecraftWindow window, IReadOnlyList<KeyboardAction> actions, CancellationToken cancellationToken);
}