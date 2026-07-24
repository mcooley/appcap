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
                new ScreenshotRequest { TargetName = "cam", OutputPath = @"C:\shots\a.png", IncludeCursor = true, Caption = "hi", Crop = new CropRectangle(10, 20, 300, 200) },
                WorkerProtocolJsonContext.Default.ScreenshotRequest);
            await JsonRpcCodec.WriteRequestAsync(client, screenshotRequest, cts.Token);
            JsonRpcResponse? screenshotResponse = await JsonRpcCodec.ReadResponseAsync(client, cts.Token);
            ScreenshotResult? ack = JsonRpcCodec.ReadResult(screenshotResponse!.Result!.Value, WorkerProtocolJsonContext.Default.ScreenshotResult);

            Assert.True(ack!.Acknowledged);
            Assert.Equal(@"C:\shots\a.png", host.LastScreenshot!.OutputPath);
            Assert.True(host.LastScreenshot.IncludeCursor);
            Assert.Equal("hi", host.LastScreenshot.Caption);
            Assert.Equal(new CropRectangle(10, 20, 300, 200), host.LastScreenshot.Crop);
        }
        finally
        {
            client.Dispose();
            await cts.CancelAsync();
            await DrainAsync(serve);
        }
    }

    [Fact]
    public async Task TargetServerHandlesStatusCaptureAndInputInProcess()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        byte[] pixels = [9, 8, 7, 6, 5, 4, 3, 2];
        FakeTarget target = new(new CapturedFrame(2, 1, pixels));

        (Stream client, Stream server) = InProcDuplexTransport.CreatePair();
        Task serve = TargetServer.ServeAsync(server, target, cts.Token);
        try
        {
            TargetClient targetClient = new(client);
            TargetStatusResult status = await targetClient.GetStatusAsync(cts.Token);
            Assert.Equal(TargetProtocol.Version, status!.ProtocolVersion);
            Assert.Equal(["touch", "keyboard"], status.SupportedInputDevices);

            CapturedFrame captured = await targetClient.CaptureFrameAsync(includeCursor: true, cts.Token);
            Assert.Equal(2, captured.Width);
            Assert.Equal(1, captured.Height);
            Assert.Equal(pixels, captured.BgraPixels);
            Assert.True(target.LastIncludeCursor);

            await targetClient.AttachInputDeviceAsync(InputDeviceType.Touch, cts.Token);
            await targetClient.AttachInputDeviceAsync(InputDeviceType.Keyboard, cts.Token);
            IReadOnlyList<InputDeviceStatus> devices = await targetClient.ListInputDevicesAsync(cts.Token);
            Assert.Collection(
                devices,
                device =>
                {
                    Assert.Equal(InputDeviceType.Touch, device.DeviceType);
                    Assert.True(device.Attached);
                },
                device =>
                {
                    Assert.Equal(InputDeviceType.Keyboard, device.DeviceType);
                    Assert.True(device.Attached);
                });

            await targetClient.TapAsync(10, 20, null, cts.Token);
            await targetClient.TypeAsync("abc[Enter]", null, cts.Token);

            Assert.Equal(InputDeviceType.Keyboard, target.LastAttachedDeviceType);
            Assert.Equal((10, 20, (InputDeviceType?)null), target.LastTap);
            Assert.Equal(("abc[Enter]", (InputDeviceType?)null), target.LastType);
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
