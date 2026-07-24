using System.Text.Json;

namespace AppCap.Protocol.Target;

// Server side of the **target protocol** (worker <-> target). Dispatches target methods
// (status, capture_frame, and input/device operations) to an ITargetHost and owns the JSON-RPC framing so that a
// target reached over any transport speaks an identical, documented wire protocol. This
// is the reference server that a remote target host would run; when the target is in-proc
// the worker calls ITarget directly instead (the optimized path — see docs/architecture.md).
internal static class TargetServer
{
    // Reads target-protocol requests from the stream and answers them until the peer
    // closes the connection.
    public static async Task ServeAsync(Stream stream, ITargetHost target, CancellationToken cancellationToken)
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
    public static async Task HandleAsync(Stream stream, JsonRpcRequest request, ITargetHost target, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case TargetMethods.Status:
                await JsonRpcCodec.WriteResponseAsync(
                    stream,
                    JsonRpcCodec.CreateSuccess(
                        request.Id,
                        new TargetStatusResult
                        {
                            SupportedInputDevices = target.SupportedInputDevices.Select(static deviceType => deviceType.ToString()).ToArray(),
                        },
                        TargetProtocolJsonContext.Default.TargetStatusResult),
                    cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.CaptureFrame:
                await HandleCaptureFrameAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.AttachInputDevice:
                await HandleAttachInputDeviceAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.RemoveInputDevice:
                await HandleRemoveInputDeviceAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.ListInputDevices:
                await HandleListInputDevicesAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.Tap:
                await HandleTapAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
                return;

            case TargetMethods.Type:
                await HandleTypeAsync(stream, request, target, cancellationToken).ConfigureAwait(false);
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

    private static async Task HandleAttachInputDeviceAsync(Stream stream, JsonRpcRequest request, ITargetHost target, CancellationToken cancellationToken)
    {
        InputDeviceParams parameters = JsonRpcCodec.ReadParams(request.Params, TargetProtocolJsonContext.Default.InputDeviceParams) ?? new InputDeviceParams();
        JsonRpcResponse response = await ExecuteInputCommandAsync(
            request,
            async token => await target.AttachInputDeviceAsync(ParseDeviceType(parameters.DeviceType), token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleRemoveInputDeviceAsync(Stream stream, JsonRpcRequest request, ITargetHost target, CancellationToken cancellationToken)
    {
        InputDeviceParams parameters = JsonRpcCodec.ReadParams(request.Params, TargetProtocolJsonContext.Default.InputDeviceParams) ?? new InputDeviceParams();
        JsonRpcResponse response = await ExecuteInputCommandAsync(
            request,
            async token => await target.RemoveInputDeviceAsync(ParseDeviceType(parameters.DeviceType), token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleListInputDevicesAsync(Stream stream, JsonRpcRequest request, ITargetHost target, CancellationToken cancellationToken)
    {
        JsonRpcResponse response;
        try
        {
            IReadOnlyList<InputDeviceStatus> devices = await target.ListInputDevicesAsync(cancellationToken).ConfigureAwait(false);
            InputDeviceListResult result = new()
            {
                Devices = devices
                    .Select(static device => new InputDeviceStateDto { DeviceType = device.DeviceType.ToString(), Attached = device.Attached })
                    .ToArray(),
            };
            response = JsonRpcCodec.CreateSuccess(request.Id, result, TargetProtocolJsonContext.Default.InputDeviceListResult);
        }
        catch (ProtocolErrorException exception)
        {
            response = JsonRpcCodec.CreateError(request.Id, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response = JsonRpcCodec.CreateError(request.Id, JsonRpcErrorCodes.InputFailed, exception.Message);
        }

        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleTapAsync(Stream stream, JsonRpcRequest request, ITargetHost target, CancellationToken cancellationToken)
    {
        PointerInputParams parameters = JsonRpcCodec.ReadParams(request.Params, TargetProtocolJsonContext.Default.PointerInputParams) ?? new PointerInputParams();
        JsonRpcResponse response = await ExecuteInputCommandAsync(
            request,
            token => target.TapAsync(parameters.X, parameters.Y, ParseOptionalDeviceType(parameters.DeviceType), token),
            cancellationToken).ConfigureAwait(false);
        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleTypeAsync(Stream stream, JsonRpcRequest request, ITargetHost target, CancellationToken cancellationToken)
    {
        KeyboardInputParams parameters = JsonRpcCodec.ReadParams(request.Params, TargetProtocolJsonContext.Default.KeyboardInputParams) ?? new KeyboardInputParams();
        JsonRpcResponse response = await ExecuteInputCommandAsync(
            request,
            token => target.TypeAsync(parameters.TextAndKeys, ParseOptionalDeviceType(parameters.DeviceType), token),
            cancellationToken).ConfigureAwait(false);
        await JsonRpcCodec.WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonRpcResponse> ExecuteInputCommandAsync(
        JsonRpcRequest request,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            return JsonRpcCodec.CreateSuccess(request.Id, new TargetCommandResult { Acknowledged = true }, TargetProtocolJsonContext.Default.TargetCommandResult);
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
