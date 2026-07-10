using AppCap.Protocol;
using AppCap.Protocol.Target;
using AppCap.Protocol.Worker;

namespace AppCap.Tests;

// Proves both protocol servers can run entirely in-process over the in-proc transport,
// speaking the identical JSON-RPC framing and codec used over a named pipe. This is what
// lets the non-recording screenshot path (worker protocol) and a future remote target
// (target protocol) reuse the same codec, dispatch, and DTOs as the on-the-wire paths.
public sealed class InProcServerTests
{
    [Fact]
    public async Task WorkerServerHandlesStatusAndScreenshotInProcess()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        FakeWorkerHost host = new(recording: ["cam"]);

        (Stream client, Stream server) = InProcDuplexTransport.CreatePair();
        Task serve = WorkerServer.ServeAsync(server, host, cts.Token);
        try
        {
            JsonRpcRequest statusRequest = JsonRpcCodec.CreateRequest(
                WorkerMethods.RecordingStatus,
                1,
                new TargetRequest { TargetName = "cam" },
                WorkerProtocolJsonContext.Default.TargetRequest);
            await JsonRpcCodec.WriteRequestAsync(client, statusRequest, cts.Token);
            JsonRpcResponse? statusResponse = await JsonRpcCodec.ReadResponseAsync(client, cts.Token);
            RecordingStatusResult? status = JsonRpcCodec.ReadResult(statusResponse!.Result!.Value, WorkerProtocolJsonContext.Default.RecordingStatusResult);
            Assert.True(status!.Recording);

            JsonRpcRequest screenshotRequest = JsonRpcCodec.CreateRequest(
                WorkerMethods.Screenshot,
                2,
                new ScreenshotRequest { TargetName = "cam", OutputPath = @"C:\shots\a.png", IncludeCursor = true, Caption = "hi" },
                WorkerProtocolJsonContext.Default.ScreenshotRequest);
            await JsonRpcCodec.WriteRequestAsync(client, screenshotRequest, cts.Token);
            JsonRpcResponse? screenshotResponse = await JsonRpcCodec.ReadResponseAsync(client, cts.Token);
            ScreenshotResult? ack = JsonRpcCodec.ReadResult(screenshotResponse!.Result!.Value, WorkerProtocolJsonContext.Default.ScreenshotResult);

            Assert.True(ack!.Acknowledged);
            Assert.Equal(@"C:\shots\a.png", host.LastScreenshot!.OutputPath);
            Assert.True(host.LastScreenshot.IncludeCursor);
            Assert.Equal("hi", host.LastScreenshot.Caption);
        }
        finally
        {
            client.Dispose();
            await cts.CancelAsync();
            await DrainAsync(serve);
        }
    }

    [Fact]
    public async Task TargetServerHandlesStatusAndCaptureFrameInProcess()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        byte[] pixels = [9, 8, 7, 6, 5, 4, 3, 2];
        FakeTarget target = new(new CapturedFrame(2, 1, pixels));

        (Stream client, Stream server) = InProcDuplexTransport.CreatePair();
        Task serve = TargetServer.ServeAsync(server, target, cts.Token);
        try
        {
            JsonRpcRequest statusRequest = JsonRpcCodec.CreateRequest(TargetMethods.Status, 1);
            await JsonRpcCodec.WriteRequestAsync(client, statusRequest, cts.Token);
            JsonRpcResponse? statusResponse = await JsonRpcCodec.ReadResponseAsync(client, cts.Token);
            TargetStatusResult? status = JsonRpcCodec.ReadResult(statusResponse!.Result!.Value, TargetProtocolJsonContext.Default.TargetStatusResult);
            Assert.Equal(TargetProtocol.Version, status!.ProtocolVersion);

            JsonRpcRequest captureRequest = JsonRpcCodec.CreateRequest(
                TargetMethods.CaptureFrame,
                2,
                new CaptureFrameParams { IncludeCursor = true },
                TargetProtocolJsonContext.Default.CaptureFrameParams);
            await JsonRpcCodec.WriteRequestAsync(client, captureRequest, cts.Token);
            JsonRpcResponse? captureResponse = await JsonRpcCodec.ReadResponseAsync(client, cts.Token);
            CaptureFrameResult? result = JsonRpcCodec.ReadResult(captureResponse!.Result!.Value, TargetProtocolJsonContext.Default.CaptureFrameResult);

            Assert.Equal(2, result!.Width);
            Assert.Equal(1, result.Height);
            Assert.Equal(Convert.ToBase64String(pixels), result.PixelsBase64);
            Assert.True(target.LastIncludeCursor);
        }
        finally
        {
            client.Dispose();
            await cts.CancelAsync();
            await DrainAsync(serve);
        }
    }

    private static async Task DrainAsync(Task serve)
    {
        try
        {
            await serve;
        }
        catch (Exception)
        {
        }
    }
}
