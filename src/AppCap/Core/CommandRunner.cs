namespace AppCap;

public sealed class CommandRunner : ICommandRunner
{
    private readonly ITargetResolver targetResolver;
    private readonly IWindowController windowController;
    private readonly IInputController inputController;
    private readonly IScreenshotCapture screenshotCapture;
    private readonly IRecordingController recordingController;

    public CommandRunner(
        ITargetResolver targetResolver,
        IWindowController windowController,
        IInputController inputController,
        IScreenshotCapture screenshotCapture,
        IRecordingController recordingController)
    {
        this.targetResolver = targetResolver;
        this.windowController = windowController;
        this.inputController = inputController;
        this.screenshotCapture = screenshotCapture;
        this.recordingController = recordingController;
    }

    public async Task<CommandExecutionResult> RunAsync(AppCapCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
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
            default:
                throw new AppCapException("Unsupported command.", ExitCodes.UsageError);
        }

        return CommandExecutionResult.Empty;
    }

    private Task AttachInputDeviceAsync(InputDeviceAttachCommand command, CancellationToken cancellationToken) =>
        inputController.AttachInputDeviceAsync(command.Target, command.DeviceType, cancellationToken);

    private Task RemoveInputDeviceAsync(InputDeviceRemoveCommand command, CancellationToken cancellationToken) =>
        inputController.RemoveInputDeviceAsync(command.Target, command.DeviceType, cancellationToken);

    private async Task<CommandExecutionResult> ListInputDevicesAsync(InputDeviceListCommand command, CancellationToken cancellationToken)
    {
        IReadOnlyList<InputDeviceStatus> devices = await inputController.ListInputDevicesAsync(command.Target, cancellationToken).ConfigureAwait(false);
        string output = string.Join(
            Environment.NewLine,
            devices.Select(device => $"{device.DeviceType}: {(device.Attached ? "attached" : "detached")}"));
        return new CommandExecutionResult(output);
    }

    private Task TapAsync(TapCommand command, CancellationToken cancellationToken) =>
        inputController.TapAsync(command.Target, command.X, command.Y, command.DeviceType, cancellationToken);

    private Task TypeAsync(TypeCommand command, CancellationToken cancellationToken) =>
        inputController.TypeAsync(command.Target, command.TextAndKeys, command.DeviceType, cancellationToken);

    private async Task ResizeAsync(ResizeCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await windowController.ResizeAsync(window, command.Width, command.Height, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScreenshotAsync(ScreenshotCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await screenshotCapture.CapturePngAsync(window, command.OutputPath, !command.ExcludeCursor, command.Caption, command.Crop, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordStartAsync(RecordStartCommand command, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveAsync(command.Target, cancellationToken).ConfigureAwait(false);
        await recordingController.StartAsync(window, command.OutputPath, command.TimeLimit, !command.ExcludeCursor, command.Crop, cancellationToken).ConfigureAwait(false);
    }

    private Task RecordStopAsync(RecordStopCommand command, CancellationToken cancellationToken) =>
        recordingController.StopAsync(command.Target, cancellationToken);

    private Task RecordCancelAsync(RecordCancelCommand command, CancellationToken cancellationToken) =>
        recordingController.CancelAsync(command.Target, cancellationToken);

    private Task RecordCaptionAsync(RecordCaptionCommand command, CancellationToken cancellationToken) =>
        recordingController.AddCaptionAsync(command.Target, command.Caption, cancellationToken);
}