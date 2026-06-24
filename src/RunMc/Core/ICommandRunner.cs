namespace RunMc;

public interface ICommandRunner
{
    Task RunAsync(RunMcCommand command, CancellationToken cancellationToken);
}