namespace RunMc;

public interface IKeyboardInputInjector
{
    Task TypeAsync(TargetWindow window, IReadOnlyList<KeyboardAction> actions, CancellationToken cancellationToken);
}