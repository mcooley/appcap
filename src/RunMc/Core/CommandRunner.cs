namespace RunMc;

public sealed class CommandRunner : ICommandRunner
{
    private readonly ITargetResolver targetResolver;
    private readonly IWindowController windowController;
    private readonly IInputInjector inputInjector;
    private readonly ICursorMover cursorMover;
    private readonly IKeyboardInputInjector keyboardInputInjector;
    private readonly IScreenshotCapture screenshotCapture;
    private readonly IRecordingController recordingController;

    public CommandRunner(
        ITargetResolver targetResolver,
        IWindowController windowController,
        IInputInjector inputInjector,
        ICursorMover cursorMover,
        IKeyboardInputInjector keyboardInputInjector,
        IScreenshotCapture screenshotCapture,
        IRecordingController recordingController)
    {
        this.targetResolver = targetResolver;
        this.windowController = windowController;
        this.inputInjector = inputInjector;
        this.cursorMover = cursorMover;
        this.keyboardInputInjector = keyboardInputInjector;
        this.screenshotCapture = screenshotCapture;
        this.recordingController = recordingController;
    }

    public async Task RunAsync(RunMcCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case FocusCommand focus:
                await FocusAsync(focus, cancellationToken).ConfigureAwait(false);
                break;
            case ClickCommand click:
                await ClickAsync(click, cancellationToken).ConfigureAwait(false);
                break;
            case HoverCommand hover:
                await HoverAsync(hover, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCommand type:
                await TypeAsync(type, cancellationToken).ConfigureAwait(false);
                break;
            case ResizeCommand resize:
                await ResizeAsync(resize, cancellationToken).ConfigureAwait(false);
                break;
            case ScreenshotCommand screenshot:
                await ScreenshotAsync(screenshot, cancellationToken).ConfigureAwait(false);
                break;
            case RecordStartCommand recordStart:
                await RecordStartAsync(recordStart, cancellationToken).ConfigureAwait(false);
                break;
            case RecordStopCommand recordStop:
                await RecordStopAsync(recordStop, cancellationToken).ConfigureAwait(false);
                break;
            case RecordCancelCommand recordCancel:
                await RecordCancelAsync(recordCancel, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new RunMcException("Unsupported command.", ExitCodes.UsageError);
        }
    }

    private async Task FocusAsync(FocusCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClickAsync(ClickCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);

        WindowBounds bounds = await windowController.GetBoundsAsync(window, cancellationToken).ConfigureAwait(false);
        if (command.X >= bounds.Width || command.Y >= bounds.Height)
        {
            throw new RunMcException("Click coordinates are outside the target window.", ExitCodes.UsageError);
        }

        int screenX = bounds.Left + command.X;
        int screenY = bounds.Top + command.Y;
        await inputInjector.ClickAsync(window, screenX, screenY, cancellationToken).ConfigureAwait(false);
    }

    private async Task HoverAsync(HoverCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);

        WindowBounds bounds = await windowController.GetBoundsAsync(window, cancellationToken).ConfigureAwait(false);
        if (command.X >= bounds.Width || command.Y >= bounds.Height)
        {
            throw new RunMcException("Hover coordinates are outside the target window.", ExitCodes.UsageError);
        }

        int screenX = bounds.Left + command.X;
        int screenY = bounds.Top + command.Y;
        await cursorMover.MoveToAsync(window, screenX, screenY, cancellationToken).ConfigureAwait(false);
    }

    private async Task TypeAsync(TypeCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);
        await keyboardInputInjector.TypeAsync(window, command.Actions, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResizeAsync(ResizeCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.ResizeAsync(window, command.Width, command.Height, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScreenshotAsync(ScreenshotCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await screenshotCapture.CapturePngAsync(window, command.OutputPath, command.IncludeCursor, command.Caption, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordStartAsync(RecordStartCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await recordingController.StartAsync(window, command.OutputPath, cancellationToken).ConfigureAwait(false);
    }

    private Task RecordStopAsync(RecordStopCommand command, CancellationToken cancellationToken) =>
        recordingController.StopAsync(command.Target, cancellationToken);

    private Task RecordCancelAsync(RecordCancelCommand command, CancellationToken cancellationToken) =>
        recordingController.CancelAsync(command.Target, cancellationToken);
}