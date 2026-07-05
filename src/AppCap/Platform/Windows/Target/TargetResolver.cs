using AppCap;
using System.Diagnostics;

namespace AppCap.Windows;

public sealed class TargetResolver : ITargetResolver
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private readonly IWindowFinder windowFinder;
    private readonly IAppLauncher appLauncher;

    public TargetResolver(IWindowFinder windowFinder, IAppLauncher appLauncher)
    {
        this.windowFinder = windowFinder;
        this.appLauncher = appLauncher;
    }

    public async Task<TargetWindow> ResolveAsync(TargetConfiguration target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (AppCapTargetConfig application in target.Applications)
        {
            TargetWindow? runningWindow = windowFinder.TryFindWindow(target, application);
            if (runningWindow is not null)
            {
                return runningWindow;
            }
        }

        foreach (AppCapTargetConfig application in target.Applications)
        {
            TargetWindow? window = await ResolveInstalledAsync(target, application, cancellationToken).ConfigureAwait(false);
            if (window is not null)
            {
                return window;
            }
        }

        throw new AppCapException($"Window was not found for target '{TargetFormatter.Format(target)}'.");
    }

    private async Task<TargetWindow?> ResolveInstalledAsync(TargetConfiguration target, AppCapTargetConfig application, CancellationToken cancellationToken)
    {
        appLauncher.LaunchAumid(application.Id);

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < LaunchTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TargetWindow? runningWindow = windowFinder.TryFindWindow(target, application);
            if (runningWindow is not null)
            {
                return runningWindow;
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}