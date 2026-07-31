using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppCap.Protocol;

// JSON-RPC 2.0 message types shared by both AppCap protocols (the internal worker
// protocol and the documented target protocol). These are a faithful subset of the
// JSON-RPC 2.0 specification
// (https://www.jsonrpc.org/specification): every message carries "jsonrpc": "2.0",
// requests carry a "method" and correlation "id", and responses carry either a
// "result" or an "error" with the matching "id". Keeping these types transport
// agnostic lets other tools implement the same protocol over a different transport
// (for example, to capture from a remote target).

internal static class JsonRpcConstants
{
    // The JSON-RPC protocol version string that must appear on every message.
    public const string Version = "2.0";
}

// The reserved JSON-RPC 2.0 error codes plus the implementation-defined server
// error codes used by the AppCap protocols. Codes in the -32000..-32099
// range are reserved by the spec for implementation-defined server errors.
internal static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // The worker failed to stop or cancel the recording (for example, the
    // encoder failed or produced no output). The human-readable reason is carried
    // in the error "message".
    public const int RecordingFailed = -32000;

    // A frame capture or screenshot failed (for example, the target window could not
    // be captured). The human-readable reason is carried in the error "message".
    public const int CaptureFailed = -32001;

    // No recording is running for the requested target, so a stop/cancel/screenshot
    // could not be served. This is distinct from a failure: the client treats it as
    // "nothing to stop".
    public const int NotRecording = -32002;

    // The requested input device type is not supported by the target.
    public const int UnsupportedInputDevice = -32003;

    // The requested input device is already attached to the target.
    public const int InputDeviceAlreadyAttached = -32004;

    // The requested input device is not attached to the target.
    public const int InputDeviceNotAttached = -32005;

    // The requested input device does not match the command's required device type.
    public const int InvalidInputDeviceSelection = -32006;

    // Input injection or input-device state management failed.
    public const int InputFailed = -32007;

    public const int TargetAlreadyAttached = -32008;

    public const int TargetNotAttached = -32009;
}

// A JSON-RPC 2.0 request object. "id" and "params" are stored as raw JSON so the
// protocol layer stays independent of any particular method's parameter shape and
// can echo back whatever id kind (number or string) the peer supplied.
internal sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = JsonRpcConstants.Version;

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

// A JSON-RPC 2.0 response object. Exactly one of "result" or "error" is populated;
// "id" mirrors the request it answers (or null for errors raised before the id
// could be determined, such as a parse error).
internal sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = JsonRpcConstants.Version;

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

// A JSON-RPC 2.0 error object carried by a failed response.
internal sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}
