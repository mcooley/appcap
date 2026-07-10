using System.Text.Json;

namespace AppCap.Protocol.Target;

// Server side of the **target protocol** (worker <-> target). Dispatches target methods
// (status and capture_frame) to an ITarget and owns the JSON-RPC framing so that a
// target reached over any transport speaks an identical, documented wire protocol. This
// is the reference server that a remote target host would run; when the target is in-proc
// the worker calls ITarget directly instead (the optimized path — see docs/architecture.md).
internal static class TargetServer
{
    // Reads target-protocol requests from the stream and answers them until the peer
    // closes the connection.
    public static async Task ServeAsync(Stream stream, ITarget target, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            JsonRpcRequest? request;
            try
            {
                request = await JsonRpcCodec.ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                await JsonRpcCodec.WriteResponseAsync(
                    stream,
                    JsonRpcCodec.CreateError(null, JsonRpcErrorCodes.ParseError, "Invalid JSON-RPC request."),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (request is null)
            {
                return;
            }

            await HandleAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
        }
    }

    // Answers a single target-protocol request by writing its JSON-RPC response to the
    // stream.
    public static async Task HandleAsync(Stream stream, JsonRpcRequest request, ITarget target, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case TargetMethods.Status:
                await JsonRpcCodec.WriteResponseAsync(
                    stream,
                    JsonRpcCodec.CreateSuccess(request.Id, new TargetStatusResult(), TargetProtocolJsonContext.Default.TargetStatusResult),
                    cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.CaptureFrame:
                await HandleCaptureFrameAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
                return;

            default:
                await JsonRpcCodec.WriteResponseAsync(
                    stream,
                    JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'."),
                    cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private static async Task HandleCaptureFrameAsync(Stream stream, JsonRpcRequest request, ITarget target, CancellationToken cancellationToken)
    {
        CaptureFrameParams parameters = JsonRpcCodec.ReadParams(request.Params, TargetProtocolJsonContext.Default.CaptureFrameParams) ?? new CaptureFrameParams();

        JsonRpcResponse response;
        try
        {
            CapturedFrame frame = await target.CaptureFrameAsync(parameters.IncludeCursor, cancellationToken).ConfigureAwait(false);
            CaptureFrameResult result = new()
            {
                Width = frame.Width,
                Height = frame.Height,
                PixelsBase64 = Convert.ToBase64String(frame.BgraPixels),
            };
            response = JsonRpcCodec.CreateSuccess(request.Id, result, TargetProtocolJsonContext.Default.CaptureFrameResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response = JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.CaptureFailed, exception.Message);
        }

        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }
}
