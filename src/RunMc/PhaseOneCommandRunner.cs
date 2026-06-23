namespace RunMc;

public sealed class PhaseOneCommandRunner : IPhaseOneCommandRunner
{
    private readonly IMinecraftTargetResolver targetResolver;
    private readonly IWindowController windowController;
    private readonly IInputInjector inputInjector;
    private readonly IScreenshotCapture screenshotCapture;

    public PhaseOneCommandRunner(
        IMinecraftTargetResolver targetResolver,
        IWindowController windowController,
        IInputInjector inputInjector,
        IScreenshotCapture screenshotCapture)
    {
        this.targetResolver = targetResolver;
        this.windowController = windowController;
        this.inputInjector = inputInjector;
        this.screenshotCapture = screenshotCapture;
    }

    public async Task RunAsync(RunMcCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsurePhaseOneTarget(command.Target);

        switch (command)
        {
            case FocusCommand focus:
                await FocusAsync(focus, cancellationToken).ConfigureAwait(false);
                break;
            case ClickCommand click:
                await ClickAsync(click, cancellationToken).ConfigureAwait(false);
                break;
            case ResizeCommand resize:
                await ResizeAsync(resize, cancellationToken).ConfigureAwait(false);
                break;
            case ScreenshotCommand screenshot:
                await ScreenshotAsync(screenshot, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new RunMcException("Unsupported command.", ExitCodes.UsageError);
        }
    }

    private static void EnsurePhaseOneTarget(TargetKind target)
    {
        if (target is TargetKind.Default or TargetKind.RunningBedrock or TargetKind.InstalledBedrock)
        {
            return;
        }

        throw new RunMcException($"Target '{TargetKindFormatter.Format(target)}' is not supported in phase 1.");
    }

    private async Task FocusAsync(FocusCommand command, CancellationToken cancellationToken)
    {
        MinecraftWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClickAsync(ClickCommand command, CancellationToken cancellationToken)
    {
        MinecraftWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        WindowBounds bounds = await windowController.GetBoundsAsync(window, cancellationToken).ConfigureAwait(false);
        if (command.X >= bounds.Width || command.Y >= bounds.Height)
        {
            throw new RunMcException("Click coordinates are outside the Minecraft window.", ExitCodes.UsageError);
        }

        await inputInjector.ClickAsync(window, command.X, command.Y, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResizeAsync(ResizeCommand command, CancellationToken cancellationToken)
    {
        MinecraftWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.ResizeAsync(window, command.Width, command.Height, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScreenshotAsync(ScreenshotCommand command, CancellationToken cancellationToken)
    {
        MinecraftWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await screenshotCapture.CapturePngAsync(window, command.OutputPath, cancellationToken).ConfigureAwait(false);
    }
}