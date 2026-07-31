using AppCap.Protocol.Worker;

namespace AppCap.Windows;

public sealed class WorkerInputController : IInputController
{
    public async Task AttachInputDeviceAsync(TargetApplication target, InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        await RecordingIpc.AttachInputDeviceAsync(CreateTargetRequest(target), deviceType, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveInputDeviceAsync(TargetApplication target, InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        await RecordingIpc.RemoveInputDeviceAsync(CreateTargetRequest(target), deviceType, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return await RecordingIpc.ListInputDevicesAsync(CreateTargetRequest(target), cancellationToken).ConfigureAwait(false);
    }

    public async Task TapAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        await RecordingIpc.TapAsync(CreateTargetRequest(target), x, y, deviceType, cancellationToken).ConfigureAwait(false);
    }

    public async Task TypeAsync(TargetApplication target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(textAndKeys);
        await RecordingIpc.TypeAsync(CreateTargetRequest(target), textAndKeys, deviceType, cancellationToken).ConfigureAwait(false);
    }

    private static TargetDescriptorRequest CreateTargetRequest(TargetApplication target) => new()
    {
        TargetName = target.Name,
        ApplicationId = target.Id,
    };
}
