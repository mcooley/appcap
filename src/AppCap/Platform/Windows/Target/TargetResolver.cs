using AppCap;
using System.Diagnostics;

namespace AppCap.Windows;

public sealed class TargetResolver : ITargetResolver
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private readonly IWindowFinder windowFinder;
    private readonly ITargetLauncher targetLauncher;

    public TargetResolver(IWindowFinder windowFinder, ITargetLauncher targetLauncher)
    {
        this.windowFinder = windowFinder;
        this.targetLauncher = targetLauncher;
    }

    public async Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        TargetWindow? runningWindow = windowFinder.TryFindWindow(target);
        if (runningWindow is not null)
        {
            return runningWindow;
        }

        TargetWindow? launchedWindow = await ResolveInstalledAsync(target, cancellationToken).ConfigureAwait(false);
        if (launchedWindow is not null)
        {
            return launchedWindow;
        }

        throw new AppCapException($"Window was not found for target '{TargetFormatter.Format(target)}'.");
    }

    public Task<TargetWindow> ResolveRunningAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            windowFinder.TryFindWindow(target) ??
            throw new AppCapException($"Target '{TargetFormatter.Format(target)}' is attached, but its application is not running."));
    }

    private async Task<TargetWindow?> ResolveInstalledAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        targetLauncher.Launch(target);

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < LaunchTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TargetWindow? runningWindow = windowFinder.TryFindWindow(target);
            if (runningWindow is not null)
            {
                return runningWindow;
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}