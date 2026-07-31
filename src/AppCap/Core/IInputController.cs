namespace AppCap;

public interface IInputController
{
    Task AttachInputDeviceAsync(TargetApplication target, InputDeviceType deviceType, CancellationToken cancellationToken);

    Task RemoveInputDeviceAsync(TargetApplication target, InputDeviceType deviceType, CancellationToken cancellationToken);

    Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetApplication target, CancellationToken cancellationToken);

    Task TapAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);

    Task MoveMouseAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);

    Task ClickMouseAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);

    Task TypeAsync(TargetApplication target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken);
}
