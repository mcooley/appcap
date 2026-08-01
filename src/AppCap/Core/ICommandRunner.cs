namespace AppCap;

public interface ICommandRunner
{
    Task<CommandExecutionResult> RunAsync(AppCapCommand command, CancellationToken cancellationToken);
}