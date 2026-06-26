namespace AppCap;

public interface ICommandRunner
{
    Task RunAsync(AppCapCommand command, CancellationToken cancellationToken);
}