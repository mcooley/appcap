namespace AppCap;

public interface ITargetResolver
{
    Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken);
}