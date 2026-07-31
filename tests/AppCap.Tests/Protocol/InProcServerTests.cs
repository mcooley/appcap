using AppCap.Protocol;
using AppCap.Protocol.Target;

namespace AppCap.Tests;

// Proves the worker-to-target protocol can run in-process over the same framing and codec
// that a future remote target transport will use.
public sealed class InProcServerTests
{
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
            Assert.Equal(["touch", "keyboard", "mouse"], status.SupportedInputDevices);

            CapturedFrame captured = await targetClient.CaptureFrameAsync(includeCursor: true, cts.Token);
            Assert.Equal(2, captured.Width);
            Assert.Equal(1, captured.Height);
            Assert.Equal(pixels, captured.BgraPixels);
            Assert.True(target.LastIncludeCursor);

            await targetClient.AttachInputDeviceAsync(InputDeviceType.Touch, cts.Token);
            await targetClient.AttachInputDeviceAsync(InputDeviceType.Keyboard, cts.Token);
            await targetClient.AttachInputDeviceAsync(InputDeviceType.Mouse, cts.Token);
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
                },
                device =>
                {
                    Assert.Equal(InputDeviceType.Mouse, device.DeviceType);
                    Assert.True(device.Attached);
                });

            await targetClient.TapAsync(10, 20, null, cts.Token);
            await targetClient.MoveMouseAsync(30, 40, null, cts.Token);
            await targetClient.ClickMouseAsync(50, 60, null, cts.Token);
            await targetClient.TypeAsync("abc[Enter]", null, cts.Token);

            Assert.Equal(InputDeviceType.Mouse, target.LastAttachedDeviceType);
            Assert.Equal((10, 20, (InputDeviceType?)null), target.LastTap);
            Assert.Equal((30, 40, (InputDeviceType?)null), target.LastMouseMove);
            Assert.Equal((50, 60, (InputDeviceType?)null), target.LastMouseClick);
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
