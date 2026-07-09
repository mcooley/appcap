using AppCap.Windows;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AppCap.Tests;

// Exercises the internal worker protocol (client <-> worker) over its named-pipe
// transport, driving the recording command listener with hand-written JSON-RPC to prove
// the wire framing is correct. The worker protocol is deliberately undocumented, but its
// framing is still validated here so the client and worker halves stay in sync.
public sealed class WorkerProtocolInteropTests
{
    [Fact]
    public async Task RawJsonRpcClientDrivesStatusAndStop()
    {
        string target = Guid.NewGuid().ToString();
        string pipeName = RecordingIpc.GetPipeName(target);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));

        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target, new FakeWorkerService(isRecording: true));
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        // A hand-written status request gets a spec-compliant response with a numeric id.
        string statusReply = await SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"recording.status\"}", cts.Token);
        using (JsonDocument document = JsonDocument.Parse(statusReply))
        {
            JsonElement root = document.RootElement;
            Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.Equal(7, root.GetProperty("id").GetInt32());
            Assert.True(root.GetProperty("result").GetProperty("recording").GetBoolean());
        }

        // A hand-written stop request with a string id is delivered to the listener and
        // its id kind is echoed back verbatim, matching the JSON-RPC id contract.
        Task<string> stopReplyTask = SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"recording.stop\"}", cts.Token);

        using RecordingIpc.RecordingStopRequest stopRequest = await waitForStop;
        Assert.Equal(RecordingIpc.RecordingStopMode.Save, stopRequest.Mode);
        await stopRequest.AcknowledgeAsync(cts.Token);

        using JsonDocument stopDocument = JsonDocument.Parse(await stopReplyTask);
        Assert.Equal("abc", stopDocument.RootElement.GetProperty("id").GetString());
        Assert.True(stopDocument.RootElement.GetProperty("result").GetProperty("acknowledged").GetBoolean());
    }

    [Fact]
    public async Task UnknownMethodReturnsMethodNotFound()
    {
        string target = Guid.NewGuid().ToString();
        string pipeName = RecordingIpc.GetPipeName(target);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));

        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target, new FakeWorkerService(isRecording: true));
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        string reply = await SendRawAsync(pipeName, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"recording.explode\"}", cts.Token);
        using (JsonDocument document = JsonDocument.Parse(reply))
        {
            Assert.Equal(-32601, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        // Stop the listener so its waiting task completes cleanly.
        Task<bool> stopClient = RecordingIpc.SendStopAsync(target, cts.Token);
        using RecordingIpc.RecordingStopRequest stopRequest = await waitForStop;
        await stopRequest.AcknowledgeAsync(cts.Token);
        Assert.True(await stopClient);
    }

    [Fact]
    public async Task RawJsonRpcClientRequestsScreenshot()
    {
        string target = Guid.NewGuid().ToString();
        string pipeName = RecordingIpc.GetPipeName(target);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));

        FakeWorkerService service = new(isRecording: true);
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target, service);
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        string reply = await SendRawAsync(
            pipeName,
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"screenshot\",\"params\":{\"outputPath\":\"C:\\\\shots\\\\live.png\",\"includeCursor\":true,\"caption\":\"live\"}}",
            cts.Token);
        using (JsonDocument document = JsonDocument.Parse(reply))
        {
            Assert.Equal(5, document.RootElement.GetProperty("id").GetInt32());
            Assert.True(document.RootElement.GetProperty("result").GetProperty("acknowledged").GetBoolean());
        }

        // The worker owns the file: it received the destination path and options verbatim.
        Assert.Equal(@"C:\shots\live.png", service.LastScreenshot!.OutputPath);
        Assert.True(service.LastScreenshot.IncludeCursor);
        Assert.Equal("live", service.LastScreenshot.Caption);

        // Stop the listener so its waiting task completes cleanly.
        Task<bool> stopClient = RecordingIpc.SendStopAsync(target, cts.Token);
        using RecordingIpc.RecordingStopRequest stopRequest = await waitForStop;
        await stopRequest.AcknowledgeAsync(cts.Token);
        Assert.True(await stopClient);
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
