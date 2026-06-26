namespace AppCap;

public interface ITargetResolver
{
    Task<TargetWindow> ResolveAsync(TargetConfiguration target, CancellationToken cancellationToken);
}