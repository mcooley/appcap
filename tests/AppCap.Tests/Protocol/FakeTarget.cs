using AppCap;
using AppCap.Protocol.Target;

namespace AppCap.Tests;

internal sealed class FakeTarget : ITargetHost
{
    private static readonly InputDeviceType[] SupportedDevices = [InputDeviceType.Touch, InputDeviceType.Keyboard, InputDeviceType.Mouse];
    private readonly CapturedFrame frame;
    private readonly InputDeviceAttachmentRegistry attachments = new("fake-target", SupportedDevices);

    public FakeTarget(CapturedFrame? frame = null)
    {
        this.frame = frame ?? new CapturedFrame(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
    }

    public bool? LastIncludeCursor { get; private set; }

    public InputDeviceType? LastAttachedDeviceType { get; private set; }

    public InputDeviceType? LastRemovedDeviceType { get; private set; }

    public (int X, int Y, InputDeviceType? DeviceType)? LastTap { get; private set; }

    public (int X, int Y, InputDeviceType? DeviceType)? LastMouseMove { get; private set; }

    public (int X, int Y, InputDeviceType? DeviceType)? LastMouseClick { get; private set; }

    public (string TextAndKeys, InputDeviceType? DeviceType)? LastType { get; private set; }

    public IReadOnlyList<InputDeviceType> SupportedInputDevices => SupportedDevices;

    public bool HasAttachedInputDevices => attachments.HasAttachedDevices;

    public Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        LastIncludeCursor = includeCursor;
        return Task.FromResult(frame);
    }

    public Task AttachInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        LastAttachedDeviceType = deviceType;
        attachments.Attach(deviceType);
        return Task.CompletedTask;
    }

    public Task RemoveInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        LastRemovedDeviceType = deviceType;
        attachments.Remove(deviceType);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(attachments.List());

    public Task TapAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        _ = attachments.Select(InputDeviceType.Touch, deviceType, "tap");
        LastTap = (x, y, deviceType);
        return Task.CompletedTask;
    }

    public Task MoveMouseAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        _ = attachments.Select(InputDeviceType.Mouse, deviceType, "mouseto");
        LastMouseMove = (x, y, deviceType);
        return Task.CompletedTask;
    }

    public Task ClickMouseAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        _ = attachments.Select(InputDeviceType.Mouse, deviceType, "click");
        LastMouseClick = (x, y, deviceType);
        return Task.CompletedTask;
    }

    public Task TypeAsync(string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        _ = attachments.Select(InputDeviceType.Keyboard, deviceType, "type");
        LastType = (textAndKeys, deviceType);
        return Task.CompletedTask;
    }
}
