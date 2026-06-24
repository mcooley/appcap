using RunMc;
using System.Diagnostics;

namespace RunMc.Windows;

public sealed class BedrockTargetResolver : IMinecraftTargetResolver
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private readonly IMinecraftWindowFinder windowFinder;
    private readonly IAppLauncher appLauncher;

    public BedrockTargetResolver(IMinecraftWindowFinder windowFinder, IAppLauncher appLauncher)
    {
        this.windowFinder = windowFinder;
        this.appLauncher = appLauncher;
    }

    public async Task<MinecraftWindow> ResolveAsync(TargetKind target, CancellationToken cancellationToken)
    {
        return target switch
        {
            TargetKind.Default => await ResolveDefaultAsync(cancellationToken).ConfigureAwait(false),
            TargetKind.RunningBedrock or TargetKind.RunningBedrockPreview or TargetKind.RunningEducation => FindRunningMinecraft(target),
            TargetKind.InstalledBedrock or TargetKind.InstalledBedrockPreview or TargetKind.InstalledEducation => await ResolveInstalledMinecraftAsync(target, cancellationToken).ConfigureAwait(false),
            _ => throw new RunMcException($"Target '{TargetKindFormatter.Format(target)}' is not supported in phase 1."),
        };
    }

    private async Task<MinecraftWindow> ResolveDefaultAsync(CancellationToken cancellationToken)
    {
        TargetKind[] runningTargets = [TargetKind.RunningBedrock, TargetKind.RunningBedrockPreview, TargetKind.RunningEducation];
        foreach (TargetKind runningTarget in runningTargets)
        {
            MinecraftWindow? runningWindow = windowFinder.TryFindWindow(BedrockPackage.FamilyNameFor(runningTarget), runningTarget);
            if (runningWindow is not null)
            {
                return runningWindow;
            }
        }

        TargetKind[] installedTargets = [TargetKind.InstalledBedrock, TargetKind.InstalledBedrockPreview, TargetKind.InstalledEducation];
        foreach (TargetKind installedTarget in installedTargets)
        {
            try
            {
                return await ResolveInstalledMinecraftAsync(installedTarget, cancellationToken).ConfigureAwait(false);
            }
            catch (RunMcException) when (installedTarget is not TargetKind.InstalledEducation)
            {
            }
        }

        throw new RunMcException("Minecraft window was not found for target 'default'.");
    }

    private MinecraftWindow FindRunningMinecraft(TargetKind target)
    {
        string packageFamilyName = BedrockPackage.FamilyNameFor(target);
        return windowFinder.TryFindWindow(packageFamilyName, target)
            ?? throw new RunMcException($"Minecraft window was not found for target '{TargetKindFormatter.Format(target)}'.");
    }

    private async Task<MinecraftWindow> ResolveInstalledMinecraftAsync(TargetKind target, CancellationToken cancellationToken)
    {
        TargetKind runningTarget = RunningTargetFor(target);
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

        throw new RunMcException($"Minecraft window was not found for target '{TargetKindFormatter.Format(target)}'.");
    }

    private static TargetKind RunningTargetFor(TargetKind target) => target switch
    {
        TargetKind.InstalledBedrockPreview => TargetKind.RunningBedrockPreview,
        TargetKind.InstalledEducation => TargetKind.RunningEducation,
        _ => TargetKind.RunningBedrock,
    };
}