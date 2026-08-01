namespace AppCap;

public sealed class CommandRunner : ICommandRunner
{
    private readonly ITargetResolver targetResolver;
    private readonly IWindowController windowController;
    private readonly IInputController inputController;
    private readonly IScreenshotCapture screenshotCapture;
    private readonly IRecordingController recordingController;
    private readonly ITargetSessionController? targetSessionController;

    public CommandRunner(
        ITargetResolver targetResolver,
        IWindowController windowController,
        IInputController inputController,
        IScreenshotCapture screenshotCapture,
        IRecordingController recordingController,
        ITargetSessionController? targetSessionController = null)
    {
        this.targetResolver = targetResolver;
        this.windowController = windowController;
        this.inputController = inputController;
        this.screenshotCapture = screenshotCapture;
        this.recordingController = recordingController;
        this.targetSessionController = targetSessionController;
    }

    public async Task<CommandExecutionResult> RunAsync(AppCapCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case TargetAttachCommand targetAttach:
                return await AttachTargetAsync(targetAttach, cancellationToken).ConfigureAwait(false);
            case TargetLaunchCommand targetLaunch:
                return await LaunchTargetAsync(targetLaunch, cancellationToken).ConfigureAwait(false);
            case TargetDetachCommand targetDetach:
                return await DetachTargetAsync(targetDetach, cancellationToken).ConfigureAwait(false);
            case TargetListCommand:
                return await ListTargetsAsync(cancellationToken).ConfigureAwait(false);
            case InputDeviceAttachCommand attach:
                await AttachInputDeviceAsync(attach, cancellationToken).ConfigureAwait(false);
                break;
            case InputDeviceRemoveCommand remove:
                await RemoveInputDeviceAsync(remove, cancellationToken).ConfigureAwait(false);
                break;
            case InputDeviceListCommand list:
                return await ListInputDevicesAsync(list, cancellationToken).ConfigureAwait(false);
            case TapCommand tap:
                await TapAsync(tap, cancellationToken).ConfigureAwait(false);
                break;
            case MouseMoveCommand mouseMove:
                await MoveMouseAsync(mouseMove, cancellationToken).ConfigureAwait(false);
                break;
            case MouseClickCommand mouseClick:
                await ClickMouseAsync(mouseClick, cancellationToken).ConfigureAwait(false);
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
            case RecordCaptionCommand recordCaption:
                await RecordCaptionAsync(recordCaption, cancellationToken).ConfigureAwait(false);
                break;
            case RecordStatusCommand recordStatus:
                return await RecordStatusAsync(recordStatus, cancellationToken).ConfigureAwait(false);
            default:
                throw new AppCapException("Unsupported command.", ExitCodes.UsageError);
        }

        return CommandExecutionResult.Empty;
    }

    private async Task<CommandExecutionResult> AttachTargetAsync(TargetAttachCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await GetTargetSessionController().AttachAsync(command.Target, command.Launch, cancellationToken).ConfigureAwait(false);
        return new CommandExecutionResult($"Attached target '{target.Name}'.");
    }

    private async Task<CommandExecutionResult> LaunchTargetAsync(TargetLaunchCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await GetTargetSessionController().LaunchAsync(command.Target, cancellationToken).ConfigureAwait(false);
        return new CommandExecutionResult($"Launched target '{target.Name}'.");
    }

    private async Task<CommandExecutionResult> DetachTargetAsync(TargetDetachCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await GetTargetSessionController().DetachAsync(command.Target, cancellationToken).ConfigureAwait(false);
        return new CommandExecutionResult($"Detached target '{target.Name}'.");
    }

    private async Task<CommandExecutionResult> ListTargetsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TargetSessionStatus> targets = await GetTargetSessionController().ListAsync(cancellationToken).ConfigureAwait(false);
        return new CommandExecutionResult(string.Join(
            Environment.NewLine,
            targets.Select(static status => $"{status.Target.Name}: {(status.Attached ? "attached" : "detached")}, {(status.Running ? "running" : "stopped")}")));
    }

    private ITargetSessionController GetTargetSessionController() =>
        targetSessionController ?? throw new AppCapException("Target sessions are unavailable.", ExitCodes.OperationalError);

    private async Task AttachInputDeviceAsync(InputDeviceAttachCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await inputController.AttachInputDeviceAsync(target, command.DeviceType, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveInputDeviceAsync(InputDeviceRemoveCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await inputController.RemoveInputDeviceAsync(target, command.DeviceType, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandExecutionResult> ListInputDevicesAsync(InputDeviceListCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<InputDeviceStatus> devices = await inputController.ListInputDevicesAsync(target, cancellationToken).ConfigureAwait(false);
        string output = string.Join(
            Environment.NewLine,
            devices.Select(device => $"{device.DeviceType}: {(device.Attached ? "attached" : "detached")}"));
        return new CommandExecutionResult(output);
    }

    private async Task TapAsync(TapCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await inputController.TapAsync(target, command.X, command.Y, command.DeviceType, cancellationToken).ConfigureAwait(false);
    }

    private async Task MoveMouseAsync(MouseMoveCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await inputController.MoveMouseAsync(target, command.X, command.Y, command.DeviceType, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClickMouseAsync(MouseClickCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await inputController.ClickMouseAsync(target, command.X, command.Y, command.DeviceType, cancellationToken).ConfigureAwait(false);
    }

    private async Task TypeAsync(TypeCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await inputController.TypeAsync(target, command.TextAndKeys, command.DeviceType, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResizeAsync(ResizeCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        TargetWindow window = await targetResolver.ResolveRunningAsync(target, cancellationToken).ConfigureAwait(false);
        await windowController.ResizeAsync(window, command.Width, command.Height, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScreenshotAsync(ScreenshotCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        TargetWindow window = await targetResolver.ResolveRunningAsync(target, cancellationToken).ConfigureAwait(false);
        await screenshotCapture.CapturePngAsync(window, command.OutputPath, !command.ExcludeCursor, command.Caption, command.Crop, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordStartAsync(RecordStartCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        TargetWindow window = await targetResolver.ResolveRunningAsync(target, cancellationToken).ConfigureAwait(false);
        await recordingController.StartAsync(window, command.OutputPath, command.TimeLimit, !command.ExcludeCursor, command.Crop, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordStopAsync(RecordStopCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await recordingController.StopAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordCancelAsync(RecordCancelCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await recordingController.CancelAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordCaptionAsync(RecordCaptionCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await recordingController.AddCaptionAsync(target, command.Caption, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandExecutionResult> RecordStatusAsync(RecordStatusCommand command, CancellationToken cancellationToken)
    {
        TargetApplication target = await ResolveCommandTargetAsync(command.Target, cancellationToken).ConfigureAwait(false);
        RecordingStatus status = await recordingController.GetStatusAsync(target, cancellationToken).ConfigureAwait(false);
        List<string> lines = [$"status: {status.Status}"];
        if (status.OutputPath is not null)
        {
            lines.Add($"output: {status.OutputPath}");
        }

        if (status.Error is not null)
        {
            lines.Add($"error: {status.Error}");
        }

        return new CommandExecutionResult(string.Join(Environment.NewLine, lines));
    }

    private Task<TargetApplication> ResolveCommandTargetAsync(TargetApplication? target, CancellationToken cancellationToken)
    {
        if (targetSessionController is not null)
        {
            return targetSessionController.ResolveAsync(target, cancellationToken);
        }

        return Task.FromResult(target ?? throw new AppCapException("No target was selected.", ExitCodes.UsageError));
    }
}