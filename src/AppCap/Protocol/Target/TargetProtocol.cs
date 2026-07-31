using System.Text.Json.Serialization;

namespace AppCap.Protocol.Target;

// The AppCap **target protocol**: the JSON-RPC 2.0 method names and result shapes that
// a target (the OS-integration component that captures frames and injects input for one
// application) exposes to a worker. This is the *documented, versioned* seam between the
// worker and a target: a target may be implemented by a different tool on a different OS
// (for example, capturing an Android device from Windows), so this contract is stable and
// published. The full protocol, including its transport bindings, is documented in
// docs/target-protocol.md.
internal static class TargetProtocol
{
    // The version of the AppCap target protocol described by these types. Bump this when
    // the wire contract changes so a worker and target can negotiate or reject mismatches.
    public const string Version = "3.0";
}

// The JSON-RPC method names understood by a target.
internal static class TargetMethods
{
    // Reports the target's availability, protocol version, and supported input devices.
    public const string Status = "target.status";

    // Captures a single frame and returns it as raw image data. The worker is
    // responsible for any further processing (captioning, encoding, saving).
    public const string CaptureFrame = "target.capture_frame";

    public const string AttachInputDevice = "target.input_device.attach";
    public const string RemoveInputDevice = "target.input_device.remove";
    public const string ListInputDevices = "target.input_device.list";
    public const string Tap = "target.input.tap";
    public const string Type = "target.input.type";
}

// Parameters for a target.capture_frame call.
internal sealed class CaptureFrameParams
{
    [JsonPropertyName("includeCursor")]
    public bool IncludeCursor { get; set; }
}

internal sealed class InputDeviceParams
{
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;
}

internal sealed class PointerInputParams
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
}

internal sealed class KeyboardInputParams
{
    [JsonPropertyName("textAndKeys")]
    public string TextAndKeys { get; set; } = string.Empty;

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
}

internal sealed class InputDeviceStateDto
{
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;

    [JsonPropertyName("attached")]
    public bool Attached { get; set; }
}

internal sealed class InputDeviceListResult
{
    [JsonPropertyName("devices")]
    public InputDeviceStateDto[] Devices { get; set; } = [];
}

internal sealed class TargetCommandResult
{
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; }
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

    [JsonPropertyName("supportedInputDevices")]
    public string[] SupportedInputDevices { get; set; } = [];
}

// A captured frame in application-facing form: raw BGRA8 premultiplied pixels plus its
// dimensions. This is what a target produces and what the worker consumes; base64 encoding
// happens only at the wire boundary. An in-proc target may instead hand the worker a GPU
// surface directly (see the frame-handoff optimization in docs/architecture.md); this
// serialized form is what a *remote* target uses.
internal sealed record CapturedFrame(int Width, int Height, byte[] BgraPixels);

// Capture-only target capability used by the optimized in-process recording path.
internal interface ITarget
{
    Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken);
}

// Worker-facing target capability that backs the documented target protocol. Capture can
// still be served directly in-proc through ITarget, but input operations and device state
// are intentionally modeled here so the worker can route them over the target protocol even
// for local targets.
internal interface ITargetHost : ITarget
{
    IReadOnlyList<InputDeviceType> SupportedInputDevices { get; }

    bool HasAttachedInputDevices { get; }

    Task AttachInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken);

    Task RemoveInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken);

    Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(CancellationToken cancellationToken);

    Task TapAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken);


    Task TypeAsync(string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken);
}
