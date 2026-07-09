using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppCap.Protocol;

// Source-generated (NativeAOT-safe) serialization metadata for the shared JSON-RPC 2.0
// envelope types used by both AppCap protocols. Reflection-based serialization is
// unavailable under PublishAot, so the envelope must be declared here; each protocol
// registers its own method params/results in its own context (WorkerProtocolJsonContext,
// TargetProtocolJsonContext).
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(long))]
internal sealed partial class JsonRpcJsonContext : JsonSerializerContext;
