using System.Text.Json.Serialization;

namespace AppCap.Protocol.Worker;

// The AppCap **worker protocol**: the JSON-RPC 2.0 method names and result shapes that a
// worker (the component that owns the shared application logic — file I/O, media encoding,
// caption rendering) exposes to the client (the CLI). This is the *internal* seam between
// the client and the worker: the client and worker are always built and shipped together,
// so unlike the target protocol this contract is undocumented and carries no backwards- or
// forwards-compatibility guarantees. It is optimized for development simplicity. See
// docs/architecture.md.
//
// A worker is reached either over a Windows named pipe (a separate recording worker
// process) or over an in-proc duplex stream (a worker hosted in the client process when no
// long-running background task is needed). The same messages and codec run over either.
internal static class WorkerMethods
{
    // Reports whether the worker is currently recording. Never fails while the worker is
    // reachable; the absence of a reachable worker means no recording is running.
    public const string RecordingStatus = "recording.status";

    // Stops the recording and saves the output file, then acknowledges once the file is
    // finalized. Fails with JsonRpcErrorCodes.RecordingFailed if finalization fails.
    public const string RecordingStop = "recording.stop";

    // Stops the recording and discards any partial output, then acknowledges. Fails with
    // JsonRpcErrorCodes.RecordingFailed if the worker cannot cancel cleanly.
    public const string RecordingCancel = "recording.cancel";

    // Captures a single frame, renders an optional caption, and saves it to the requested
    // path. The worker owns the file I/O and rendering; the client only supplies the
    // destination and options. Serving this never starts a second capture session: a
    // recording worker serves it from its live session, and an in-proc worker captures the
    // window directly.
    public const string Screenshot = "screenshot";
}

// Parameters for a screenshot call. The worker resolves the frame from its target, renders
// the optional caption, and writes the PNG to OutputPath (an absolute path, since the
// worker may run with a different working directory than the client).
internal sealed class ScreenshotRequest
{
    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("includeCursor")]
    public bool IncludeCursor { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }
}

// Result of a screenshot call. "acknowledged" is always true on success (the file is on
// disk); failures are reported through the JSON-RPC error channel instead.
internal sealed class ScreenshotResult
{
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; }
}

// Result of a recording.status call.
internal sealed class RecordingStatusResult
{
    [JsonPropertyName("recording")]
    public bool Recording { get; set; }
}

// Result of a recording.stop or recording.cancel call. "acknowledged" is always true on
// success; failures are reported through the JSON-RPC error channel instead.
internal sealed class RecordingCommandResult
{
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; }
}

// The client-facing capability a worker exposes to the shared worker-protocol dispatcher.
// Implementations own the actual work (capturing via a target, rendering, saving) while
// the dispatcher owns the JSON-RPC framing, so a worker hosted in-proc and a worker in the
// recording process answer identically.
internal interface IWorkerService
{
    bool IsRecording { get; }

    Task CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken);
}
