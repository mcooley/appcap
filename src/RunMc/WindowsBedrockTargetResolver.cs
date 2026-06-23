using System.Diagnostics;

namespace RunMc;

public sealed class WindowsBedrockTargetResolver : IMinecraftTargetResolver
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private readonly IWindowsMinecraftWindowFinder windowFinder;
    private readonly IWindowsAppLauncher appLauncher;

    public WindowsBedrockTargetResolver(IWindowsMinecraftWindowFinder windowFinder, IWindowsAppLauncher appLauncher)
    {
        this.windowFinder = windowFinder;
        this.appLauncher = appLauncher;
    }

    public async Task<MinecraftWindow> ResolveAsync(TargetKind target, CancellationToken cancellationToken)
    {
        return target switch
        {
            TargetKind.Default => await ResolveDefaultAsync(cancellationToken).ConfigureAwait(false),
            TargetKind.RunningBedrock => FindRunningBedrock(),
            TargetKind.InstalledBedrock => await ResolveInstalledBedrockAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new RunMcException($"Target '{TargetKindFormatter.Format(target)}' is not supported in phase 1."),
        };
    }

    private async Task<MinecraftWindow> ResolveDefaultAsync(CancellationToken cancellationToken)
    {
        MinecraftWindow? runningWindow = windowFinder.TryFindWindow(BedrockPackage.FamilyName, TargetKind.RunningBedrock);
        return runningWindow ?? await ResolveInstalledBedrockAsync(cancellationToken).ConfigureAwait(false);
    }

    private MinecraftWindow FindRunningBedrock()
    {
        return windowFinder.TryFindWindow(BedrockPackage.FamilyName, TargetKind.RunningBedrock)
            ?? throw new RunMcException("Minecraft Bedrock window was not found.");
    }

    private async Task<MinecraftWindow> ResolveInstalledBedrockAsync(CancellationToken cancellationToken)
    {
        MinecraftWindow? runningWindow = windowFinder.TryFindWindow(BedrockPackage.FamilyName, TargetKind.RunningBedrock);
        if (runningWindow is not null)
        {
            return runningWindow;
        }

        appLauncher.LaunchAumid(BedrockPackage.Aumid);

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < LaunchTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runningWindow = windowFinder.TryFindWindow(BedrockPackage.FamilyName, TargetKind.RunningBedrock);
            if (runningWindow is not null)
            {
                return runningWindow;
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new RunMcException("Minecraft Bedrock window was not found.");
    }
}