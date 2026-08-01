namespace AppCap;

public sealed record TargetSessionStatus(TargetApplication Target, bool Attached, bool Running);

public interface ITargetSessionController
{
    Task<TargetApplication> AttachAsync(TargetApplication? target, bool launch, CancellationToken cancellationToken);

    Task<TargetApplication> LaunchAsync(TargetApplication? target, CancellationToken cancellationToken);

    Task<TargetApplication> DetachAsync(TargetApplication? target, CancellationToken cancellationToken);

    Task<IReadOnlyList<TargetSessionStatus>> ListAsync(CancellationToken cancellationToken);

    Task<TargetApplication> ResolveAsync(TargetApplication? target, CancellationToken cancellationToken);
}