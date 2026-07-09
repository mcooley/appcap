using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AppCap.Protocol;

// Encodes and decodes JSON-RPC 2.0 messages over a byte stream for the AppCap
// protocols. Framing is one compact UTF-8 JSON message per line (terminated by "\n"),
// which keeps the protocol simple to implement over any bidirectional stream. This is
// the single place that knows the wire format, so the client and server stay in sync
// and other transports can reuse it.
internal static class JsonRpcCodec
{
    // Builds a request that carries a numeric id and no parameters.
    public static JsonRpcRequest CreateRequest(string method, long id) => new()
    {
        Method = method,
        Id = CreateNumericId(id),
    };

    // Builds a request that carries a numeric id and a typed parameter object.
    public static JsonRpcRequest CreateRequest<TParams>(string method, long id, TParams parameters, JsonTypeInfo<TParams> paramsTypeInfo) => new()
    {
        Method = method,
        Id = CreateNumericId(id),
        Params = JsonSerializer.SerializeToElement(parameters, paramsTypeInfo),
    };

    // Deserializes a JSON-RPC params element into a concrete parameter type, or returns
    // null when the request carried no params.
    public static TParams? ReadParams<TParams>(JsonElement? parameters, JsonTypeInfo<TParams> paramsTypeInfo)
        where TParams : class =>
        parameters is { } element ? element.Deserialize(paramsTypeInfo) : null;

    // Wraps a numeric id as a JSON number element, matching the JSON-RPC id contract.
    public static JsonElement CreateNumericId(long id) =>
        JsonSerializer.SerializeToElement(id, JsonRpcJsonContext.Default.Int64);

    // Builds a successful response echoing the request id and carrying the given result.
    public static JsonRpcResponse CreateSuccess<TResult>(JsonElement? id, TResult result, JsonTypeInfo<TResult> resultTypeInfo) => new()
    {
        Id = id,
        Result = JsonSerializer.SerializeToElement(result, resultTypeInfo),
    };

    // Builds an error response echoing the request id and carrying the error detail.
    public static JsonRpcResponse CreateError(JsonElement? id, int code, string message) => new()
    {
        Id = id,
        Error = new JsonRpcError { Code = code, Message = message },
    };

    // Deserializes a JSON-RPC result element into a concrete result type.
    public static TResult? ReadResult<TResult>(JsonElement result, JsonTypeInfo<TResult> resultTypeInfo) =>
        result.Deserialize(resultTypeInfo);

    public static async Task WriteRequestAsync(Stream stream, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(request, JsonRpcJsonContext.Default.JsonRpcRequest);
        await WriteLineAsync(stream, json, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteResponseAsync(Stream stream, JsonRpcResponse response, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(response, JsonRpcJsonContext.Default.JsonRpcResponse);
        await WriteLineAsync(stream, json, cancellationToken).ConfigureAwait(false);
    }

    // Reads a single request line. Returns null if the peer closed the stream without
    // sending one, or throws JsonException if the line is not a valid message.
    public static async Task<JsonRpcRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize(line, JsonRpcJsonContext.Default.JsonRpcRequest);
    }

    // Reads a single response line. Returns null if the peer closed the stream without
    // sending one (for example, the capture host is not running).
    public static async Task<JsonRpcResponse?> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize(line, JsonRpcJsonContext.Default.JsonRpcResponse);
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteLineAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        await using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        await writer.WriteLineAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
