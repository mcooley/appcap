using System.Text.Json.Serialization;

namespace AppCap.Protocol.Target;

// Source-generated (NativeAOT-safe) serialization metadata for the target protocol's
// method params and result types. The shared JSON-RPC envelope is registered separately
// in JsonRpcJsonContext.
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CaptureFrameParams))]
[JsonSerializable(typeof(CaptureFrameResult))]
[JsonSerializable(typeof(InputDeviceParams))]
[JsonSerializable(typeof(PointerInputParams))]
[JsonSerializable(typeof(KeyboardInputParams))]
[JsonSerializable(typeof(InputDeviceStateDto))]
[JsonSerializable(typeof(InputDeviceListResult))]
[JsonSerializable(typeof(TargetCommandResult))]
[JsonSerializable(typeof(TargetStatusResult))]
internal sealed partial class TargetProtocolJsonContext : JsonSerializerContext;
