using AppCap;
using System.Text.Json.Serialization;

namespace AppCap.Protocol.Worker;

// Source-generated (NativeAOT-safe) serialization metadata for the worker protocol's
// method params and result types. The shared JSON-RPC envelope is registered separately
// in JsonRpcJsonContext.
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RecordingStatusResult))]
[JsonSerializable(typeof(RecordingCommandResult))]
[JsonSerializable(typeof(RecordingStartRequest))]
[JsonSerializable(typeof(TargetRequest))]
[JsonSerializable(typeof(CaptionRequest))]
[JsonSerializable(typeof(TargetDescriptorRequest))]
[JsonSerializable(typeof(InputDeviceRequest))]
[JsonSerializable(typeof(PointerInputRequest))]
[JsonSerializable(typeof(KeyboardInputRequest))]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(ScreenshotRequest))]
[JsonSerializable(typeof(ScreenshotResult))]
[JsonSerializable(typeof(WorkerInputDeviceStateDto))]
[JsonSerializable(typeof(WorkerInputDeviceListResult))]
[JsonSerializable(typeof(CropRectangle))]
internal sealed partial class WorkerProtocolJsonContext : JsonSerializerContext;
