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
[JsonSerializable(typeof(ScreenshotRequest))]
[JsonSerializable(typeof(ScreenshotResult))]
internal sealed partial class WorkerProtocolJsonContext : JsonSerializerContext;
