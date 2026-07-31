using System.Text.Json.Serialization;
using AppCap;

namespace AppCap.Protocol.Worker;

// The AppCap **worker protocol**: the JSON-RPC 2.0 method names and result shapes that a
// worker (the component that owns the shared application logic — file I/O, media encoding,
// caption rendering) exposes to the client (the CLI). This is the *internal* seam between
// the client and the worker: the client and worker are always built and shipped together,
// so unlike the target protocol this contract is undocumented and carries no backwards- or
// forwards-compatibility guarantees. It is optimized for development simplicity. See
// docs/architecture.md.
//
// A worker is reached over a Windows named pipe. The single machine-wide worker process
// multiplexes attached targets and their recordings, so every target operation carries
// the target name it applies to.
internal static class WorkerMethods
{
    // Liveness probe. Always succeeds while the worker is reachable; the client uses it to
    // decide whether a worker is already running before launching one just-in-time.
    public const string Ping = "worker.ping";

    public const string TargetAttach = "target.attach";
    public const string TargetDetach = "target.detach";
    public const string TargetList = "target.list";

    // Starts a recording for a target and acknowledges once the recording is confirmed
    // running (its first frame has been captured). Fails with
    // JsonRpcErrorCodes.RecordingFailed if the target cannot be captured, no frames arrive,
    // or a recording is already running for that target.
    public const string RecordingStart = "recording.start";

    // Reports whether a recording is currently running for a target.
    public const string RecordingStatus = "recording.status";

    // Stops the recording for a target and saves the output file, then acknowledges once
    // the file is finalized. Fails with JsonRpcErrorCodes.NotRecording if no recording is
    // running for the target, or JsonRpcErrorCodes.RecordingFailed if finalization fails.
    public const string RecordingStop = "recording.stop";

    // Stops the recording for a target and discards any partial output, then acknowledges.
    // Fails with JsonRpcErrorCodes.NotRecording if no recording is running for the target,
    // or JsonRpcErrorCodes.RecordingFailed if the worker cannot cancel cleanly.
    public const string RecordingCancel = "recording.cancel";

    public const string RecordingCaption = "recording.caption";

    // Captures a single frame for a target, renders an optional caption, and saves it to
    // the requested path. The worker owns the file I/O and rendering; the client only
    // supplies the destination and options. Serving this never starts a second capture
    // session: the worker serves it from that target's live recording session. Fails with
    // JsonRpcErrorCodes.NotRecording if the target is no longer recording, so the client
    // can report that the target is unavailable.
    public const string Screenshot = "screenshot";

    public const string InputDeviceAttach = "input_device.attach";
    public const string InputDeviceRemove = "input_device.remove";
    public const string InputDeviceList = "input_device.list";
    public const string InputTap = "input.tap";
    public const string InputMouseMove = "input.mouseto";
    public const string InputMouseClick = "input.click";
    public const string InputType = "input.type";
}

// Parameters for a recording.start call. The client resolves the target window and passes
// its handle (valid across processes on the same desktop) plus the descriptor the worker
// needs to identify the target and where to write the output.
internal sealed class RecordingStartRequest
{
    [JsonPropertyName("targetName")]
    public string TargetName { get; set; } = string.Empty;

    [JsonPropertyName("applicationName")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("applicationId")]
    public string ApplicationId { get; set; } = string.Empty;

    [JsonPropertyName("windowHandle")]
    public long WindowHandle { get; set; }

    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("timeLimitSeconds")]
    public int TimeLimitSeconds { get; set; }

    [JsonPropertyName("includeCursor")]
    public bool IncludeCursor { get; set; } = true;

    [JsonPropertyName("crop")]
    public CropRectangle? Crop { get; set; }

}

// Parameters for a call that operates on a single target's recording (status/stop/cancel).
internal class TargetRequest
{
    [JsonPropertyName("targetName")]
    public string TargetName { get; set; } = string.Empty;
}

internal sealed class CaptionRequest : TargetRequest
{
    [JsonPropertyName("caption")]
    public string Caption { get; set; } = string.Empty;
}

internal class TargetDescriptorRequest : TargetRequest
{
    [JsonPropertyName("applicationId")]
    public string ApplicationId { get; set; } = string.Empty;
}

internal sealed class AttachedTargetDto
{
    [JsonPropertyName("targetName")]
    public string TargetName { get; set; } = string.Empty;

    [JsonPropertyName("applicationId")]
    public string ApplicationId { get; set; } = string.Empty;
}

internal sealed class AttachedTargetListResult
{
    [JsonPropertyName("targets")]
    public AttachedTargetDto[] Targets { get; set; } = [];
}

internal sealed class InputDeviceRequest : TargetDescriptorRequest
{
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;
}

internal sealed class PointerInputRequest : TargetDescriptorRequest
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
}

internal sealed class KeyboardInputRequest : TargetDescriptorRequest
{
    [JsonPropertyName("textAndKeys")]
    public string TextAndKeys { get; set; } = string.Empty;

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
}

// Parameters for a screenshot call. The worker resolves the frame from the target's live
// recording session, renders the optional caption, and writes the PNG to OutputPath (an
// absolute path, since the worker may run with a different working directory than the
// client).
internal sealed class ScreenshotRequest
{
    [JsonPropertyName("targetName")]
    public string TargetName { get; set; } = string.Empty;

    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("includeCursor")]
    public bool IncludeCursor { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("crop")]
    public CropRectangle? Crop { get; set; }
}

// Result of a worker.ping call. "ok" is always true when the worker answers.
internal sealed class PingResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }
}

// Result of a screenshot call. "acknowledged" is always true on success (the file is on
// disk); failures are reported through the JSON-RPC error channel instead.
internal sealed class ScreenshotResult
{
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; }
}

internal sealed class WorkerInputDeviceStateDto
{
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;

    [JsonPropertyName("attached")]
    public bool Attached { get; set; }
}

internal sealed class WorkerInputDeviceListResult
{
    [JsonPropertyName("devices")]
    public WorkerInputDeviceStateDto[] Devices { get; set; } = [];
}

// Result of a recording.status call.
internal sealed class RecordingStatusResult
{
    [JsonPropertyName("recording")]
    public bool Recording { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "never-started";

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

// Result of a recording.start, recording.stop, or recording.cancel call. "acknowledged" is
// always true on success; failures are reported through the JSON-RPC error channel instead.
internal sealed class RecordingCommandResult
{
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; }
}

// The client-facing capability a worker exposes to the shared worker-protocol dispatcher.
// Implementations own the actual work (managing recording sessions, capturing via a
// target, rendering, saving) while the dispatcher owns the JSON-RPC framing, so a worker
// hosted in-proc and the machine-wide worker process answer identically. One host serves
// many targets concurrently, so every recording method is keyed by target name.
internal interface IWorkerHost
{
    // Records liveness activity and reports the worker is up. Returns true.
    bool Ping();

    Task AttachTargetAsync(TargetDescriptorRequest target, CancellationToken cancellationToken);

    Task<bool> DetachTargetAsync(string targetName, CancellationToken cancellationToken);

    IReadOnlyList<TargetDescriptorRequest> ListTargets();

    void CompleteRequest();

    // Starts a recording for the target and returns once it is confirmed running. Throws
    // AppCapException if the target is already recording or the capture cannot start.
    Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken);

    // Stops (saving, or discarding when discard is true) the target's recording. Returns
    // true if a recording was stopped, false if no recording is running for the target.
    // Throws when the worker reports a failure while finalizing.
    Task<bool> StopRecordingAsync(string targetName, bool discard, CancellationToken cancellationToken);

    Task<bool> AddCaptionAsync(string targetName, string caption, CancellationToken cancellationToken);

    RecordingStatusResult GetRecordingStatus(string targetName);

    // Captures and saves a screenshot from the target's live recording session. Returns
    // true on success, or false if the target is no longer recording so the caller can
    // report that the target is unavailable.
    Task<bool> CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken);

    Task AttachInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken);

    Task RemoveInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken);

    Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetDescriptorRequest target, CancellationToken cancellationToken);

    Task TapAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);

    Task MoveMouseAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);

    Task ClickMouseAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);

    Task TypeAsync(TargetDescriptorRequest target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken);
}
