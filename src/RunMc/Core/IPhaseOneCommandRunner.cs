namespace RunMc;

public interface IPhaseOneCommandRunner
{
    Task RunAsync(RunMcCommand command, CancellationToken cancellationToken);
}