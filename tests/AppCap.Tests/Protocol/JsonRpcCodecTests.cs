using AppCap.Protocol;
using AppCap.Protocol.Worker;
using System.Text.Json;

namespace AppCap.Tests;

public sealed class JsonRpcCodecTests
{
    [Fact]
    public async Task RequestIsWrittenAsJsonRpc20()
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(WorkerMethods.RecordingStatus, 42);

        using MemoryStream stream = new();
        await JsonRpcCodec.WriteRequestAsync(stream, request, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(ReadFirstLine(stream));
        JsonElement root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("recording.status", root.GetProperty("method").GetString());
        Assert.Equal(42, root.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task SuccessResponseRoundTripsResult()
    {
        JsonRpcResponse response = JsonRpcCodec.CreateSuccess(
            JsonRpcCodec.CreateNumericId(1),
            new RecordingStatusResult { Recording = true },
            WorkerProtocolJsonContext.Default.RecordingStatusResult);

        using MemoryStream stream = new();
        await JsonRpcCodec.WriteResponseAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        JsonRpcResponse? parsed = await JsonRpcCodec.ReadResponseAsync(stream, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal("2.0", parsed!.JsonRpc);
        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Result);
        RecordingStatusResult? result = JsonRpcCodec.ReadResult(parsed.Result!.Value, WorkerProtocolJsonContext.Default.RecordingStatusResult);
        Assert.True(result!.Recording);
    }

    [Fact]
    public async Task ErrorResponseRoundTripsCodeAndMessage()
    {
        JsonRpcResponse response = JsonRpcCodec.CreateError(
            JsonRpcCodec.CreateNumericId(9),
            JsonRpcErrorCodes.RecordingFailed,
            "capture failed");

        using MemoryStream stream = new();
        await JsonRpcCodec.WriteResponseAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        JsonRpcResponse? parsed = await JsonRpcCodec.ReadResponseAsync(stream, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Result);
        Assert.NotNull(parsed.Error);
        Assert.Equal(JsonRpcErrorCodes.RecordingFailed, parsed.Error!.Code);
        Assert.Equal("capture failed", parsed.Error.Message);
    }

    [Fact]
    public async Task ResponseOmitsUnusedResultOrErrorMember()
    {
        JsonRpcResponse response = JsonRpcCodec.CreateSuccess(
            JsonRpcCodec.CreateNumericId(3),
            new RecordingCommandResult { Acknowledged = true },
            WorkerProtocolJsonContext.Default.RecordingCommandResult);

        using MemoryStream stream = new();
        await JsonRpcCodec.WriteResponseAsync(stream, response, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(ReadFirstLine(stream));
        Assert.True(document.RootElement.TryGetProperty("result", out _));
        Assert.False(document.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task RequestRoundTripsTypedParams()
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(
            WorkerMethods.Screenshot,
            11,
            new ScreenshotRequest { OutputPath = @"C:\shots\a.png", IncludeCursor = true, Crop = new CropRectangle(10, 20, 300, 200) },
            WorkerProtocolJsonContext.Default.ScreenshotRequest);

        using MemoryStream stream = new();
        await JsonRpcCodec.WriteRequestAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        JsonRpcRequest? parsed = await JsonRpcCodec.ReadRequestAsync(stream, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal("screenshot", parsed!.Method);
        ScreenshotRequest? parameters = JsonRpcCodec.ReadParams(parsed.Params, WorkerProtocolJsonContext.Default.ScreenshotRequest);
        Assert.NotNull(parameters);
        Assert.True(parameters!.IncludeCursor);
        Assert.Equal(@"C:\shots\a.png", parameters.OutputPath);
        Assert.Equal(new CropRectangle(10, 20, 300, 200), parameters.Crop);
    }

    [Fact]
    public void ReadParamsReturnsNullWhenAbsent()
    {
        ScreenshotRequest? parameters = JsonRpcCodec.ReadParams(null, WorkerProtocolJsonContext.Default.ScreenshotRequest);
        Assert.Null(parameters);
    }

    private static string ReadFirstLine(MemoryStream stream)
    {
        stream.Position = 0;
        using StreamReader reader = new(stream, leaveOpen: true);
        return reader.ReadLine() ?? string.Empty;
    }
}
