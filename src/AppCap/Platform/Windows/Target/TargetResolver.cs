using AppCap;
using System.Diagnostics;
using AppCap.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AppCap.Windows;

public sealed class TargetResolver : ITargetResolver
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private readonly IWindowFinder windowFinder;
    private readonly ITargetLauncher targetLauncher;
    private readonly ILogger? logger;

    public TargetResolver(IWindowFinder windowFinder, ITargetLauncher targetLauncher, ILogger? logger = null)
    {
        this.windowFinder = windowFinder;
        this.targetLauncher = targetLauncher;
        this.logger = logger;
    }

    public async Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (logger is not null)
        {
            TargetLog.ResolveStarted(logger, target.Name, target.Id);
        }

        TargetWindow? runningWindow = windowFinder.TryFindWindow(target);
        if (runningWindow is not null)
        {
            if (logger is not null)
            {
                TargetLog.ResolveSucceeded(logger, target.Name, runningWindow.Handle);
            }
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
        TargetWindow? window = windowFinder.TryFindWindow(target);
        if (window is not null)
        {
            return Task.FromResult(window);
        }

        if (logger is not null)
        {
            TargetLog.TargetNotRunning(logger, target.Name, target.Id);
        }

        throw new AppCapException($"Target '{TargetFormatter.Format(target)}' is attached, but its application is not running.");
    }

    private async Task<TargetWindow?> ResolveInstalledAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        if (logger is not null)
        {
            TargetLog.LaunchingTarget(logger, target.Name, target.Id);
        }
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

        if (logger is not null)
        {
            TargetLog.ResolveTimedOut(logger, target.Name, target.Id);
        }
        return null;
    }
}