using AppCap.Protocol;
using System.Text.Json;

namespace AppCap.Protocol.Target;

internal sealed class TargetClient
{
    private readonly Stream stream;
    private long nextRequestId;

    public TargetClient(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
    }

    public Task<TargetStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        SendAsync(
            TargetMethods.Status,
            buildRequest: requestId => JsonRpcCodec.CreateRequest(TargetMethods.Status, requestId),
            readResult: result => JsonRpcCodec.ReadResult(result, TargetProtocolJsonContext.Default.TargetStatusResult)
                ?? throw new AppCapException("The target returned an empty status response."),
            cancellationToken);

    public Task AttachInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedAsync(
            TargetMethods.AttachInputDevice,
            new InputDeviceParams { DeviceType = deviceType.ToString() },
            TargetProtocolJsonContext.Default.InputDeviceParams,
            cancellationToken);

    public Task RemoveInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedAsync(
            TargetMethods.RemoveInputDevice,
            new InputDeviceParams { DeviceType = deviceType.ToString() },
            TargetProtocolJsonContext.Default.InputDeviceParams,
            cancellationToken);

    public async Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(CancellationToken cancellationToken)
    {
        InputDeviceListResult result = await SendAsync(
            TargetMethods.ListInputDevices,
            buildRequest: requestId => JsonRpcCodec.CreateRequest(TargetMethods.ListInputDevices, requestId),
            readResult: element => JsonRpcCodec.ReadResult(element, TargetProtocolJsonContext.Default.InputDeviceListResult)
                ?? throw new AppCapException("The target returned an empty input-device list response."),
            cancellationToken).ConfigureAwait(false);

        return result.Devices
            .Select(static device => new InputDeviceStatus(InputDeviceType.Parse(device.DeviceType), device.Attached))
            .ToArray();
    }

    public Task TapAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendPointerInputAsync(TargetMethods.Tap, x, y, deviceType, cancellationToken);

    public Task TypeAsync(string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedAsync(
            TargetMethods.Type,
            new KeyboardInputParams { TextAndKeys = textAndKeys, DeviceType = deviceType?.ToString() },
            TargetProtocolJsonContext.Default.KeyboardInputParams,
            cancellationToken);

    public async Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        CaptureFrameResult result = await SendAsync(
            TargetMethods.CaptureFrame,
            buildRequest: requestId => JsonRpcCodec.CreateRequest(
                TargetMethods.CaptureFrame,
                requestId,
                new CaptureFrameParams { IncludeCursor = includeCursor },
                TargetProtocolJsonContext.Default.CaptureFrameParams),
            readResult: element => JsonRpcCodec.ReadResult(element, TargetProtocolJsonContext.Default.CaptureFrameResult)
                ?? throw new AppCapException("The target returned an empty capture response."),
            cancellationToken).ConfigureAwait(false);

        return new CapturedFrame(result.Width, result.Height, Convert.FromBase64String(result.PixelsBase64));
    }

    private Task SendPointerInputAsync(string method, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedAsync(
            method,
            new PointerInputParams { X = x, Y = y, DeviceType = deviceType?.ToString() },
            TargetProtocolJsonContext.Default.PointerInputParams,
            cancellationToken);

    private async Task SendAcknowledgedAsync<TParams>(
        string method,
        TParams parameters,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken cancellationToken)
    {
        TargetCommandResult result = await SendAsync(
            method,
            buildRequest: requestId => JsonRpcCodec.CreateRequest(method, requestId, parameters, paramsTypeInfo),
            readResult: element => JsonRpcCodec.ReadResult(element, TargetProtocolJsonContext.Default.TargetCommandResult)
                ?? throw new AppCapException($"The target returned an empty response for '{method}'."),
            cancellationToken).ConfigureAwait(false);

        if (!result.Acknowledged)
        {
            throw new AppCapException($"The target did not acknowledge '{method}'.");
        }
    }

    private async Task<TResult> SendAsync<TResult>(
        string method,
        Func<long, JsonRpcRequest> buildRequest,
        Func<JsonElement, TResult> readResult,
        CancellationToken cancellationToken)
    {
        JsonRpcRequest request = buildRequest(Interlocked.Increment(ref nextRequestId));
        await JsonRpcCodec.WriteRequestAsync(stream, request, cancellationToken).ConfigureAwait(false);

        JsonRpcResponse? response = await JsonRpcCodec.ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw new AppCapException($"The target did not respond to '{method}'.");
        }

        if (response.Error is { } error)
        {
            throw new ProtocolErrorException(error.Code, error.Message);
        }

        if (response.Result is not { } result)
        {
            throw new AppCapException($"The target returned an empty response for '{method}'.");
        }

        try
        {
            return readResult(result);
        }
        catch (JsonException exception)
        {
            throw new AppCapException($"The target returned an invalid response for '{method}'.", exception);
        }
    }
}
