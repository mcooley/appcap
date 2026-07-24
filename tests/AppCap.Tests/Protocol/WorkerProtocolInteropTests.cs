using AppCap.Windows;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AppCap.Tests;

// Exercises the internal worker protocol (client <-> worker) over its named-pipe transport,
// driving the machine worker's server loop with hand-written JSON-RPC to prove the wire
// framing is correct. The worker protocol is deliberately undocumented, but its framing is
// still validated here so the client and worker halves stay in sync. Runs serialized in the
// WorkerPipe collection with a unique pipe name so tests never contend for the same pipe.
[Collection(WorkerPipeSerialization.Name)]
public sealed class WorkerProtocolInteropTests : IDisposable
{
    public WorkerProtocolInteropTests() => RecordingIpc.PipeNameOverride = "appcap-test-" + Guid.NewGuid().ToString("N");

    public void Dispose() => RecordingIpc.PipeNameOverride = null;

    [Fact]
    public async Task RawJsonRpcClientDrivesStatusAndStop()
    {
        const string target = "cam";
        string pipeName = RecordingIpc.GetPipeName();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        FakeWorkerHost host = new(recording: [target]);
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            // A hand-written status request gets a spec-compliant response with a numeric id.
            string statusReply = await SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"recording.status\",\"params\":{\"targetName\":\"cam\"}}", cts.Token);
            using (JsonDocument document = JsonDocument.Parse(statusReply))
            {
                JsonElement root = document.RootElement;
                Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
                Assert.Equal(7, root.GetProperty("id").GetInt32());
                Assert.True(root.GetProperty("result").GetProperty("recording").GetBoolean());
            }

            // A hand-written stop request with a string id is delivered to the worker and its
            // id kind is echoed back verbatim, matching the JSON-RPC id contract.
            string stopReply = await SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"recording.stop\",\"params\":{\"targetName\":\"cam\"}}", cts.Token);
            using JsonDocument stopDocument = JsonDocument.Parse(stopReply);
            Assert.Equal("abc", stopDocument.RootElement.GetProperty("id").GetString());
            Assert.True(stopDocument.RootElement.GetProperty("result").GetProperty("acknowledged").GetBoolean());
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task UnknownMethodReturnsMethodNotFound()
    {
        string pipeName = RecordingIpc.GetPipeName();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            string reply = await SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"recording.explode\"}", cts.Token);
            using JsonDocument document = JsonDocument.Parse(reply);
            Assert.Equal(-32601, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task RawJsonRpcClientRequestsScreenshot()
    {
        const string target = "cam";
        string pipeName = RecordingIpc.GetPipeName();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        FakeWorkerHost host = new(recording: [target]);
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            string reply = await SendRawAsync(
                pipeName,
                "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"screenshot\",\"params\":{\"targetName\":\"cam\",\"outputPath\":\"C:\\\\shots\\\\live.png\",\"includeCursor\":true,\"caption\":\"live\",\"crop\":{\"x\":10,\"y\":20,\"width\":300,\"height\":200}}}",
                cts.Token);
            using (JsonDocument document = JsonDocument.Parse(reply))
            {
                Assert.Equal(5, document.RootElement.GetProperty("id").GetInt32());
                Assert.True(document.RootElement.GetProperty("result").GetProperty("acknowledged").GetBoolean());
            }

            // The worker owns the file: it received the destination path and options verbatim.
            Assert.Equal("cam", host.LastScreenshot!.TargetName);
            Assert.Equal(@"C:\shots\live.png", host.LastScreenshot.OutputPath);
            Assert.True(host.LastScreenshot.IncludeCursor);
            Assert.Equal("live", host.LastScreenshot.Caption);
            Assert.Equal(new CropRectangle(10, 20, 300, 200), host.LastScreenshot.Crop);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task RawJsonRpcClientManagesInputDevicesAndTap()
    {
        const string target = "cam";
        string pipeName = RecordingIpc.GetPipeName();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        FakeWorkerHost host = new();
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            string attachReply = await SendRawAsync(
                pipeName,
                "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"input_device.attach\",\"params\":{\"targetName\":\"cam\",\"applicationId\":\"Package_family!App\",\"deviceType\":\"touch\"}}",
                cts.Token);
            using (JsonDocument document = JsonDocument.Parse(attachReply))
            {
                Assert.Equal(9, document.RootElement.GetProperty("id").GetInt32());
                Assert.True(document.RootElement.GetProperty("result").GetProperty("acknowledged").GetBoolean());
            }

            string listReply = await SendRawAsync(
                pipeName,
                "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"input_device.list\",\"params\":{\"targetName\":\"cam\",\"applicationId\":\"Package_family!App\"}}",
                cts.Token);
            using (JsonDocument document = JsonDocument.Parse(listReply))
            {
                JsonElement devices = document.RootElement.GetProperty("result").GetProperty("devices");
                Assert.Equal(2, devices.GetArrayLength());
                Assert.Equal("touch", devices[0].GetProperty("deviceType").GetString());
                Assert.True(devices[0].GetProperty("attached").GetBoolean());
                Assert.Equal("keyboard", devices[1].GetProperty("deviceType").GetString());
                Assert.False(devices[1].GetProperty("attached").GetBoolean());
            }

            string tapReply = await SendRawAsync(
                pipeName,
                "{\"jsonrpc\":\"2.0\",\"id\":11,\"method\":\"input.tap\",\"params\":{\"targetName\":\"cam\",\"applicationId\":\"Package_family!App\",\"x\":150,\"y\":130}}",
                cts.Token);
            using (JsonDocument document = JsonDocument.Parse(tapReply))
            {
                Assert.Equal(11, document.RootElement.GetProperty("id").GetInt32());
                Assert.True(document.RootElement.GetProperty("result").GetProperty("acknowledged").GetBoolean());
            }

            Assert.Equal(target, host.LastInputDeviceAttach!.TargetName);
            Assert.Equal("touch", host.LastInputDeviceAttach.DeviceType);
            Assert.Equal(target, host.LastTap!.Value.Target.TargetName);
            Assert.Equal(150, host.LastTap.Value.X);
            Assert.Equal(130, host.LastTap.Value.Y);
            Assert.Null(host.LastTap.Value.DeviceType);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task RawJsonRpcClientPings()
    {
        string pipeName = RecordingIpc.GetPipeName();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            string reply = await SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"worker.ping\"}", cts.Token);
            using JsonDocument document = JsonDocument.Parse(reply);
            Assert.True(document.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    private static async Task ShutdownAsync(CancellationTokenSource cts, Task<bool> server)
    {
        await cts.CancelAsync();
        try
        {
            await server;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<string> SendRawAsync(string pipeName, string requestJson, CancellationToken cancellationToken)
    {
        using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);

        await using (StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" })
        {
            await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken);
        }

        using StreamReader reader = new(pipe, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
    }
}
