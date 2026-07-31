using AppCap.Protocol.Worker;

namespace AppCap.Windows;

public sealed class TargetSessionController : ITargetSessionController
{
    private readonly TargetCatalog catalog;
    private readonly IWindowFinder windowFinder;
    private readonly ITargetResolver targetResolver;

    public TargetSessionController(TargetCatalog catalog, IWindowFinder windowFinder, ITargetResolver targetResolver)
    {
        this.catalog = catalog;
        this.windowFinder = windowFinder;
        this.targetResolver = targetResolver;
    }

    public async Task<TargetApplication> AttachAsync(TargetApplication? target, bool launch, CancellationToken cancellationToken)
    {
        TargetApplication selected = target ?? catalog.Applications.FirstOrDefault(application => windowFinder.TryFindWindow(application) is not null) ?? catalog.Default;
        if (launch)
        {
            _ = await targetResolver.ResolveAsync(selected, cancellationToken).ConfigureAwait(false);
        }

        await WorkerProcessController.EnsureWorkerRunningAsync(cancellationToken).ConfigureAwait(false);
        await RecordingIpc.AttachTargetAsync(CreateRequest(selected), cancellationToken).ConfigureAwait(false);
        return selected;
    }

    public async Task<TargetApplication> DetachAsync(TargetApplication? target, CancellationToken cancellationToken)
    {
        IReadOnlyList<TargetDescriptorRequest> attached = await RecordingIpc.ListTargetsAsync(cancellationToken).ConfigureAwait(false);
        TargetApplication selected;
        if (target is not null)
        {
            if (!attached.Any(candidate => string.Equals(candidate.TargetName, target.Name, StringComparison.Ordinal)))
            {
                throw new AppCapException($"Target '{target.Name}' is not attached.", ExitCodes.UsageError);
            }

            selected = target;
        }
        else if (attached.Count is 1 && catalog.TryParse(attached[0].TargetName, out TargetApplication onlyTarget))
        {
            selected = onlyTarget;
        }
        else
        {
            throw new AppCapException(
                attached.Count is 0
                    ? "No targets are attached."
                    : "Multiple targets are attached. Specify the target name to detach.",
                ExitCodes.UsageError);
        }

        await RecordingIpc.DetachTargetAsync(selected.Name, cancellationToken).ConfigureAwait(false);
        return selected;
    }

    public async Task<IReadOnlyList<TargetSessionStatus>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TargetDescriptorRequest> attached = await RecordingIpc.ListTargetsAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> attachedNames = attached.Select(static target => target.TargetName).ToHashSet(StringComparer.Ordinal);
        return catalog.Applications
            .Select(application => new TargetSessionStatus(
                application,
                attachedNames.Contains(application.Name),
                windowFinder.TryFindWindow(application) is not null))
            .ToArray();
    }

    public async Task<TargetApplication> ResolveAsync(TargetApplication? target, CancellationToken cancellationToken)
    {
        IReadOnlyList<TargetDescriptorRequest> attached = await RecordingIpc.ListTargetsAsync(cancellationToken).ConfigureAwait(false);
        if (target is not null)
        {
            if (attached.Any(candidate => string.Equals(candidate.TargetName, target.Name, StringComparison.Ordinal)))
            {
                return target;
            }

            throw new AppCapException($"Target '{target.Name}' is not attached. Run 'appcap target attach {target.Name}' first.", ExitCodes.UsageError);
        }

        if (attached.Count is 0)
        {
            throw new AppCapException("No targets are attached. Run 'appcap target attach' first.", ExitCodes.UsageError);
        }

        if (attached.Count > 1)
        {
            throw new AppCapException("Multiple targets are attached. Use --target to select one.", ExitCodes.UsageError);
        }

        if (catalog.TryParse(attached[0].TargetName, out TargetApplication selected))
        {
            return selected;
        }

        throw new AppCapException($"Attached target '{attached[0].TargetName}' is not configured.");
    }

    private static TargetDescriptorRequest CreateRequest(TargetApplication target) =>
        new() { TargetName = target.Name, ApplicationId = target.Id };
}
