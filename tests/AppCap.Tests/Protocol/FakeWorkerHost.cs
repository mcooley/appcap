using AppCap;
using AppCap.Protocol;
using AppCap.Protocol.Worker;
using System.Collections.Concurrent;

namespace AppCap.Tests;

internal sealed class FakeWorkerHost : IWorkerHost
{
    private static readonly InputDeviceType[] SupportedInputDevices = [InputDeviceType.Touch, InputDeviceType.Keyboard];

    private readonly ConcurrentDictionary<string, byte> recordings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InputDeviceAttachmentRegistry> inputDevices = new(StringComparer.Ordinal);

    public FakeWorkerHost(IEnumerable<string>? recording = null)
    {
        if (recording is not null)
        {
            foreach (string target in recording)
            {
                recordings[target] = 0;
            }
        }
    }

    public ScreenshotRequest? LastScreenshot { get; private set; }

    public RecordingStartRequest? LastStart { get; private set; }

    public bool? LastStopDiscard { get; private set; }

    public CaptionRequest? LastCaption { get; private set; }

    public InputDeviceRequest? LastInputDeviceAttach { get; private set; }

    public InputDeviceRequest? LastInputDeviceRemove { get; private set; }

    public TargetDescriptorRequest? LastInputDeviceList { get; private set; }

    public (TargetDescriptorRequest Target, int X, int Y, InputDeviceType? DeviceType)? LastTap { get; private set; }

    public (TargetDescriptorRequest Target, string TextAndKeys, InputDeviceType? DeviceType)? LastType { get; private set; }

    public string? StartFailWith { get; set; }

    public string? StopFailWith { get; set; }

    public string? InputFailWith { get; set; }

    public string? BlockStopForTarget { get; set; }

    public TaskCompletionSource StopBlock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Ping() => true;

    public Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken)
    {
        LastStart = request;
        if (StartFailWith is not null)
        {
            throw new AppCapException(StartFailWith);
        }

        if (!recordings.TryAdd(request.TargetName, 0))
        {
            throw new AppCapException($"A recording is already running for target '{request.TargetName}'.");
        }

        return Task.CompletedTask;
    }

    public async Task<bool> StopRecordingAsync(string targetName, bool discard, CancellationToken cancellationToken)
    {
        if (!recordings.ContainsKey(targetName))
        {
            return false;
        }

        if (string.Equals(BlockStopForTarget, targetName, StringComparison.Ordinal))
        {
            await StopBlock.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        LastStopDiscard = discard;
        if (StopFailWith is not null)
        {
            throw new AppCapException(StopFailWith);
        }

        return recordings.TryRemove(targetName, out _);
    }

    public bool IsRecording(string targetName) => recordings.ContainsKey(targetName);

    public Task<bool> AddCaptionAsync(string targetName, string caption, CancellationToken cancellationToken)
    {
        LastCaption = new CaptionRequest { TargetName = targetName, Caption = caption };
        return Task.FromResult(recordings.ContainsKey(targetName));
    }

    public Task<bool> CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken)
    {
        LastScreenshot = request;
        return Task.FromResult(recordings.ContainsKey(request.TargetName));
    }

    public Task AttachInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        LastInputDeviceAttach = new InputDeviceRequest
        {
            TargetName = target.TargetName,
            ApplicationId = target.ApplicationId,
            DeviceType = deviceType.ToString(),
        };
        ThrowIfInputFailed();
        GetRegistry(target.TargetName).Attach(deviceType);
        return Task.CompletedTask;
    }

    public Task RemoveInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        LastInputDeviceRemove = new InputDeviceRequest
        {
            TargetName = target.TargetName,
            ApplicationId = target.ApplicationId,
            DeviceType = deviceType.ToString(),
        };
        ThrowIfInputFailed();
        GetRegistry(target.TargetName).Remove(deviceType);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetDescriptorRequest target, CancellationToken cancellationToken)
    {
        LastInputDeviceList = target;
        ThrowIfInputFailed();
        return Task.FromResult(GetRegistry(target.TargetName).List());
    }

    public Task TapAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        LastTap = (target, x, y, deviceType);
        ThrowIfInputFailed();
        _ = GetRegistry(target.TargetName).Select(InputDeviceType.Touch, deviceType, "tap");
        return Task.CompletedTask;
    }

    public Task TypeAsync(TargetDescriptorRequest target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        LastType = (target, textAndKeys, deviceType);
        ThrowIfInputFailed();
        _ = GetRegistry(target.TargetName).Select(InputDeviceType.Keyboard, deviceType, "type");
        return Task.CompletedTask;
    }

    private InputDeviceAttachmentRegistry GetRegistry(string targetName) =>
        inputDevices.GetOrAdd(targetName, static name => new InputDeviceAttachmentRegistry(name, SupportedInputDevices));

    private void ThrowIfInputFailed()
    {
        if (InputFailWith is not null)
        {
            throw new AppCapException(InputFailWith);
        }
    }
}
