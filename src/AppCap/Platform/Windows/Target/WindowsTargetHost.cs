using AppCap.Protocol;
using AppCap.Protocol.Target;

namespace AppCap.Windows;

internal sealed class WindowsTargetHost : ITargetHost
{
    private static readonly InputDeviceType[] SupportedDeviceTypes = [InputDeviceType.Touch, InputDeviceType.Keyboard, InputDeviceType.Mouse];

    private readonly TargetWindow window;
    private readonly IWindowController windowController;
    private readonly IInputInjector inputInjector;
    private readonly IKeyboardInputInjector keyboardInputInjector;
    private readonly InputDeviceAttachmentRegistry attachments;
    private readonly WindowCaptureTarget captureTarget;

    public WindowsTargetHost(
        TargetWindow window,
        IWindowController windowController,
        IInputInjector inputInjector,
        IKeyboardInputInjector keyboardInputInjector,
        InputDeviceAttachmentRegistry attachments)
    {
        this.window = window;
        this.windowController = windowController;
        this.inputInjector = inputInjector;
        this.keyboardInputInjector = keyboardInputInjector;
        this.attachments = attachments;
        captureTarget = new WindowCaptureTarget(window);
    }

    public static IReadOnlyList<InputDeviceType> SupportedInputDeviceTypes => SupportedDeviceTypes;

    public IReadOnlyList<InputDeviceType> SupportedInputDevices => SupportedDeviceTypes;

    public bool HasAttachedInputDevices => attachments.HasAttachedDevices;

    public Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken) =>
        captureTarget.CaptureFrameAsync(includeCursor, cancellationToken);

    public Task AttachInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        attachments.Attach(deviceType);
        return Task.CompletedTask;
    }

    public Task RemoveInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        attachments.Remove(deviceType);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(attachments.List());
    }

    public async Task TapAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        (int screenX, int screenY) = await ResolvePointerAsync("tap", x, y, deviceType, cancellationToken).ConfigureAwait(false);
        await inputInjector.TapAsync(window, screenX, screenY, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveMouseAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        (int screenX, int screenY) = await ResolvePointerAsync("mouseto", InputDeviceType.Mouse, x, y, deviceType, cancellationToken).ConfigureAwait(false);
        await inputInjector.MoveMouseAsync(window, screenX, screenY, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClickMouseAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        (int screenX, int screenY) = await ResolvePointerAsync("click", InputDeviceType.Mouse, x, y, deviceType, cancellationToken).ConfigureAwait(false);
        await inputInjector.ClickMouseAsync(window, screenX, screenY, cancellationToken).ConfigureAwait(false);
    }

    public async Task TypeAsync(string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textAndKeys);
        _ = attachments.Select(InputDeviceType.Keyboard, deviceType, "type");
        if (!KeyboardSequenceParser.TryParse(textAndKeys, out IReadOnlyList<KeyboardAction> actions, out string? errorMessage))
        {
            throw new ProtocolErrorException(JsonRpcErrorCodes.InvalidParams, errorMessage ?? "Invalid keyboard sequence.");
        }

        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);
        await keyboardInputInjector.TypeAsync(window, actions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(int ScreenX, int ScreenY)> ResolvePointerAsync(string commandName, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
        => await ResolvePointerAsync(commandName, InputDeviceType.Touch, x, y, deviceType, cancellationToken).ConfigureAwait(false);

    private async Task<(int ScreenX, int ScreenY)> ResolvePointerAsync(
        string commandName,
        InputDeviceType requiredDeviceType,
        int x,
        int y,
        InputDeviceType? deviceType,
        CancellationToken cancellationToken)
    {
        _ = attachments.Select(requiredDeviceType, deviceType, commandName);
        await windowController.BringToForegroundAsync(window, cancellationToken).ConfigureAwait(false);

        WindowBounds bounds = await windowController.GetBoundsAsync(window, cancellationToken).ConfigureAwait(false);
        if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height)
        {
            throw new ProtocolErrorException(
                JsonRpcErrorCodes.InvalidParams,
                $"{char.ToUpperInvariant(commandName[0])}{commandName[1..]} coordinates are outside the target window.");
        }

        return (bounds.Left + x, bounds.Top + y);
    }
}
