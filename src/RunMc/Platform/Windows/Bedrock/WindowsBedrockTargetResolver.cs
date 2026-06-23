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
            TargetKind.RunningBedrock or TargetKind.RunningBedrockPreview => FindRunningBedrock(target),
            TargetKind.InstalledBedrock or TargetKind.InstalledBedrockPreview => await ResolveInstalledBedrockAsync(target, cancellationToken).ConfigureAwait(false),
            _ => throw new RunMcException($"Target '{TargetKindFormatter.Format(target)}' is not supported in phase 1."),
        };
    }

    private async Task<MinecraftWindow> ResolveDefaultAsync(CancellationToken cancellationToken)
    {
        MinecraftWindow? runningWindow = windowFinder.TryFindWindow(BedrockPackage.RetailFamilyName, TargetKind.RunningBedrock);
        return runningWindow ?? await ResolveInstalledBedrockAsync(TargetKind.InstalledBedrock, cancellationToken).ConfigureAwait(false);
    }

    private MinecraftWindow FindRunningBedrock(TargetKind target)
    {
        string packageFamilyName = BedrockPackage.FamilyNameFor(target);
        return windowFinder.TryFindWindow(packageFamilyName, target)
            ?? throw new RunMcException($"Minecraft Bedrock window was not found for target '{TargetKindFormatter.Format(target)}'.");
    }

    private async Task<MinecraftWindow> ResolveInstalledBedrockAsync(TargetKind target, CancellationToken cancellationToken)
    {
        TargetKind runningTarget = target is TargetKind.InstalledBedrockPreview ? TargetKind.RunningBedrockPreview : TargetKind.RunningBedrock;
        string packageFamilyName = BedrockPackage.FamilyNameFor(runningTarget);
        MinecraftWindow? runningWindow = windowFinder.TryFindWindow(packageFamilyName, runningTarget);
        if (runningWindow is not null)
        {
            return runningWindow;
        }

        appLauncher.LaunchAumid(BedrockPackage.AumidFor(target));

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < LaunchTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runningWindow = windowFinder.TryFindWindow(packageFamilyName, runningTarget);
            if (runningWindow is not null)
            {
                return runningWindow;
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new RunMcException($"Minecraft Bedrock window was not found for target '{TargetKindFormatter.Format(target)}'.");
    }
}