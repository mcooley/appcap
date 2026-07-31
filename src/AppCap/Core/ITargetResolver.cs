namespace AppCap;

public interface ITargetResolver
{
    Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken);

    Task<TargetWindow> ResolveRunningAsync(TargetApplication target, CancellationToken cancellationToken);
}