using System.Text.Json;

namespace AppCap.Protocol.Worker;

// Server side of the **worker protocol** (client <-> worker). Dispatches every worker
// method to an IWorkerHost and owns the JSON-RPC framing, so that the machine-wide worker
// over a named pipe and a worker hosted in-proc over a DuplexStream speak an identical wire
// protocol. Because the host awaits real work before returning, the responses for
// recording.start/stop/cancel are naturally deferred until the recording is confirmed or
// finalized — no special transfer mechanism is needed.
internal static class WorkerServer
{
    // Reads worker-protocol requests from the stream and answers them until the peer
    // closes the connection. Used by workers (such as the in-proc worker host) that serve
    // a short sequence of requests over a single duplex stream.
    public static async Task ServeAsync(Stream stream, IWorkerHost host, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await HandleConnectionAsync(stream, host, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    // Reads and answers a single worker-protocol request from the stream. Returns false
    // when the peer closed the stream without sending a request (nothing more to serve),
    // true after a request was answered.
    public static async Task<bool> HandleConnectionAsync(Stream stream, IWorkerHost host, CancellationToken cancellationToken)
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
            return true;
        }

        if (request is null)
        {
            return false;
        }

        JsonRpcResponse response = await DispatchAsync(request, host, cancellationToken).ConfigureAwait(false);
        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case WorkerMethods.Ping:
                host.Ping();
                return JsonRpcCodec.CreateSuccess(request.Id, new PingResult { Ok = true }, WorkerProtocolJsonContext.Default.PingResult);

            case WorkerMethods.RecordingStart:
                return await StartAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.RecordingStatus:
            {
                TargetRequest parameters = ReadTarget(request);
                return JsonRpcCodec.CreateSuccess(
                    request.Id,
                    new RecordingStatusResult { Recording = host.IsRecording(parameters.TargetName) },
                    WorkerProtocolJsonContext.Default.RecordingStatusResult);
            }

            case WorkerMethods.RecordingStop:
                return await StopAsync(request, host, discard: false, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.RecordingCancel:
                return await StopAsync(request, host, discard: true, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.RecordingCaption:
                return await CaptionAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.Screenshot:
                return await ScreenshotAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.InputDeviceAttach:
                return await AttachInputDeviceAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.InputDeviceRemove:
                return await RemoveInputDeviceAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.InputDeviceList:
                return await ListInputDevicesAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.InputTap:
                return await TapAsync(request, host, cancellationToken).ConfigureAwait(false);

            case WorkerMethods.InputType:
                return await TypeAsync(request, host, cancellationToken).ConfigureAwait(false);

            default:
                return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'.");
        }
    }

    private static async Task<JsonRpcResponse> StartAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        RecordingStartRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.RecordingStartRequest) ?? new RecordingStartRequest();
        try
        {
            await host.StartRecordingAsync(parameters, cancellationToken).ConfigureAwait(false);
            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.RecordingFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> StopAsync(JsonRpcRequest request, IWorkerHost host, bool discard, CancellationToken cancellationToken)
    {
        TargetRequest parameters = ReadTarget(request);
        try
        {
            bool stopped = await host.StopRecordingAsync(parameters.TargetName, discard, cancellationToken).ConfigureAwait(false);
            if (!stopped)
            {
                return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.NotRecording, $"No recording is running for target '{parameters.TargetName}'.");
            }

            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.RecordingFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> CaptionAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        CaptionRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.CaptionRequest) ?? new CaptionRequest();
        try
        {
            bool captioned = await host.AddCaptionAsync(parameters.TargetName, parameters.Caption, cancellationToken).ConfigureAwait(false);
            if (!captioned)
            {
                return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.NotRecording, $"No recording is running for target '{parameters.TargetName}'.");
            }

            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.RecordingFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> ScreenshotAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        ScreenshotRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.ScreenshotRequest) ?? new ScreenshotRequest();
        try
        {
            bool captured = await host.CaptureScreenshotAsync(parameters, cancellationToken).ConfigureAwait(false);
            if (!captured)
            {
                return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.NotRecording, $"No recording is running for target '{parameters.TargetName}'.");
            }

            return JsonRpcCodec.CreateSuccess(request.Id, new ScreenshotResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.ScreenshotResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.CaptureFailed, exception.Message);
        }
    }

    private static TargetRequest ReadTarget(JsonRpcRequest request) =>
        JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.TargetRequest) ?? new TargetRequest();

    private static TargetDescriptorRequest ReadTargetDescriptor(JsonRpcRequest request) =>
        JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.TargetDescriptorRequest) ?? new TargetDescriptorRequest();

    private static async Task<JsonRpcResponse> AttachInputDeviceAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        InputDeviceRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.InputDeviceRequest) ?? new InputDeviceRequest();
        try
        {
            await host.AttachInputDeviceAsync(parameters, ParseDeviceType(parameters.DeviceType), cancellationToken).ConfigureAwait(false);
            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (ProtocolErrorException exception)
        {
            return JsonRpcCodec.CreateError(request.Id, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.InputFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> RemoveInputDeviceAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        InputDeviceRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.InputDeviceRequest) ?? new InputDeviceRequest();
        try
        {
            await host.RemoveInputDeviceAsync(parameters, ParseDeviceType(parameters.DeviceType), cancellationToken).ConfigureAwait(false);
            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (ProtocolErrorException exception)
        {
            return JsonRpcCodec.CreateError(request.Id, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.InputFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> ListInputDevicesAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        TargetDescriptorRequest parameters = ReadTargetDescriptor(request);
        try
        {
            IReadOnlyList<InputDeviceStatus> devices = await host.ListInputDevicesAsync(parameters, cancellationToken).ConfigureAwait(false);
            WorkerInputDeviceListResult result = new()
            {
                Devices = devices
                    .Select(static device => new WorkerInputDeviceStateDto { DeviceType = device.DeviceType.ToString(), Attached = device.Attached })
                    .ToArray(),
            };
            return JsonRpcCodec.CreateSuccess(request.Id, result, WorkerProtocolJsonContext.Default.WorkerInputDeviceListResult);
        }
        catch (ProtocolErrorException exception)
        {
            return JsonRpcCodec.CreateError(request.Id, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.InputFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> TapAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        PointerInputRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.PointerInputRequest) ?? new PointerInputRequest();
        try
        {
            await host.TapAsync(parameters, parameters.X, parameters.Y, ParseOptionalDeviceType(parameters.DeviceType), cancellationToken).ConfigureAwait(false);
            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (ProtocolErrorException exception)
        {
            return JsonRpcCodec.CreateError(request.Id, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.InputFailed, exception.Message);
        }
    }

    private static async Task<JsonRpcResponse> TypeAsync(JsonRpcRequest request, IWorkerHost host, CancellationToken cancellationToken)
    {
        KeyboardInputRequest parameters = JsonRpcCodec.ReadParams(request.Params, WorkerProtocolJsonContext.Default.KeyboardInputRequest) ?? new KeyboardInputRequest();
        try
        {
            await host.TypeAsync(parameters, parameters.TextAndKeys, ParseOptionalDeviceType(parameters.DeviceType), cancellationToken).ConfigureAwait(false);
            return JsonRpcCodec.CreateSuccess(request.Id, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        }
        catch (ProtocolErrorException exception)
        {
            return JsonRpcCodec.CreateError(request.Id, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.InputFailed, exception.Message);
        }
    }

    private static InputDeviceType ParseDeviceType(string value)
    {
        if (!InputDeviceType.TryParse(value, out InputDeviceType deviceType))
        {
            throw new ProtocolErrorException(JsonRpcErrorCodes.InvalidParams, "Invalid input device identifier.");
        }

        return deviceType;
    }

    private static InputDeviceType? ParseOptionalDeviceType(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseDeviceType(value);
}
