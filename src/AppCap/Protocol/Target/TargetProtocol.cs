using System.Text.Json.Serialization;

namespace AppCap.Protocol.Target;

// The AppCap **target protocol**: the JSON-RPC 2.0 method names and result shapes that
// a target (the OS-integration component that captures frames and injects input for one
// application) exposes to a worker. This is the *documented, versioned* seam between the
// worker and a target: a target may be implemented by a different tool on a different OS
// (for example, capturing an Android device from Windows), so this contract is stable and
// published, unlike the internal worker protocol. The full protocol, including its
// transport bindings, is documented in docs/target-protocol.md.
internal static class TargetProtocol
{
    // The version of the AppCap target protocol described by these types. Bump this when
    // the wire contract changes so a worker and target can negotiate or reject mismatches.
    public const string Version = "2.0";
}

// The JSON-RPC method names understood by a target.
internal static class TargetMethods
{
    // Reports the target's availability and the protocol version it speaks. Never fails
    // while the target is reachable.
    public const string Status = "target.status";

    // Captures a single frame and returns it as raw image data. The worker is
    // responsible for any further processing (captioning, encoding, saving).
    public const string CaptureFrame = "target.capture_frame";
}

// Parameters for a target.capture_frame call.
internal sealed class CaptureFrameParams
{
    [JsonPropertyName("includeCursor")]
    public bool IncludeCursor { get; set; }
}

// Result of a target.capture_frame call. The image is returned as raw, uncompressed
// BGRA8 premultiplied pixels (row-major, top-down) so the worker is free to overlay a
// caption and choose an output encoding.
internal sealed class CaptureFrameResult
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("pixelsBase64")]
    public string PixelsBase64 { get; set; } = string.Empty;

}

// Result of a target.status call.
internal sealed class TargetStatusResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = TargetProtocol.Version;
}

// A captured frame in application-facing form: raw BGRA8 premultiplied pixels plus its
// dimensions. This is what a target produces and what the worker consumes; base64 encoding
// happens only at the wire boundary. An in-proc target may instead hand the worker a GPU
// surface directly (see the frame-handoff optimization in docs/architecture.md); this
// serialized form is what a *remote* target uses.
internal sealed record CapturedFrame(int Width, int Height, byte[] BgraPixels);

// The worker-facing capability a target exposes. In-proc, the worker calls this directly
// (the optimized path); a remote target places the documented target protocol (served by
// TargetServer) between the worker and this interface.
internal interface ITarget
{
    Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken);
}
