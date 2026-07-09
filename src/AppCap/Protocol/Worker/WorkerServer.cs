using System.Text.Json;

namespace AppCap.Protocol.Worker;

// Server side of the **worker protocol** (client <-> worker). Handles the request kinds a
// worker answers immediately (recording.status and screenshot) by delegating to an
// IWorkerService, and owns the JSON-RPC framing so that a worker hosted in-proc over a
// DuplexStream and a recording worker over a named pipe speak an identical wire protocol.
// Recording lifecycle methods (stop/cancel) are not handled here: only the recording
// worker implements them, because it defers their response until the recording is
// finalized.
internal static class WorkerServer
{
    // Reads worker-protocol requests from the stream and answers them until the peer
    // closes the connection. Used by workers (such as the in-proc worker host) that serve
    // status and screenshot requests only.
    public static async Task ServeAsync(Stream stream, IWorkerService service, CancellationToken cancellationToken)
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

            await HandleAsync(stream, request, service, cancellationToken).ConfigureAwait(false);
        }
    }

    // Answers a single status, screenshot, or unknown-method request by writing its
    // JSON-RPC response to the stream. Returns false for methods this dispatcher does not
    // own (stop/cancel), leaving the caller to handle them.
    public static async Task<bool> HandleAsync(Stream stream, JsonRpcRequest request, IWorkerService service, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case WorkerMethods.RecordingStatus:
                await JsonRpcCodec.WriteResponseAsync(
                    stream,
                    JsonRpcCodec.CreateSuccess(request.Id, new RecordingStatusResult { Recording = service.IsRecording }, WorkerProtocolJsonContext.Default.RecordingStatusResult),
                    cancellationToken).ConfigureAwait(false);
                return true;

            case WorkerMethods.Screenshot:
                await HandleScreenshotAsync(stream, request, service, cancellationToken).ConfigureAwait(false);
                return true;

            case WorkerMethods.RecordingStop:
            case WorkerMethods.RecordingCancel:
                return false;

            default:
                await JsonRpcCodec.WriteResponseAsync(
                    stream,
                    JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'."),
                    cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private static async Task HandleScreenshotAsync(Stream stream, JsonRpcRequest request, IWorkerService service, CancellationToken cancellationToken)
    {
        ScreenshotRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.ScreenshotRequest) ?? new ScreenshotRequest();

        JsonRpcResponse response;
        try
        {
            await service.CaptureScreenshotAsync(parameters, cancellationToken).ConfigureAwait(false);
            response = JsonRpcCodec.CreateSuccess(request.Id, new ScreenshotResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.ScreenshotResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response = JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.CaptureFailed, exception.Message);
        }

        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }
}
