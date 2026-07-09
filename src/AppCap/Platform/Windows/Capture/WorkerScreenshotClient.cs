using AppCap;
using AppCap.Protocol;
using AppCap.Protocol.Worker;

namespace AppCap.Windows;

// Client-side IScreenshotCapture that asks a worker to capture and save a screenshot over
// the worker protocol. If a recording is running for the target it asks the recording
// worker (over the named pipe) so no second capture session is started; otherwise it hosts
// a worker in-process over an in-proc transport. Either way the *worker* owns capturing the
// frame, rendering the caption, and writing the file — the client only supplies the
// destination path and options and waits for the acknowledgement.
public sealed class WorkerScreenshotClient : IScreenshotCapture
{
    public async Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        // Send an absolute path: a recording worker may run with a different working
        // directory than this client, and it is the worker that writes the file.
        ScreenshotRequest request = new()
        {
            OutputPath = Path.GetFullPath(outputPath),
            IncludeCursor = includeCursor,
            Caption = caption,
        };

        string targetName = window.Target.Name;
        if (await RecordingIpc.IsRecordingAsync(targetName, cancellationToken).ConfigureAwait(false))
        {
            if (await RecordingIpc.CaptureScreenshotAsync(targetName, request, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            // The recording ended between the status probe and the request; fall back to
            // capturing in-process.
        }

        await CaptureInProcessAsync(window, request, cancellationToken).ConfigureAwait(false);
    }

    // Hosts a worker in-process over an in-proc transport and issues a single screenshot
    // request against it, so the non-recording path uses the very same protocol and codec
    // as the recording path — only the transport differs. The in-proc worker captures from
    // a window target directly and saves the file.
    private static async Task CaptureInProcessAsync(TargetWindow window, ScreenshotRequest request, CancellationToken cancellationToken)
    {
        (Stream client, Stream server) = InProcDuplexTransport.CreatePair();
        using CancellationTokenSource hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        WorkerService worker = new(new WindowCaptureTarget(window), isRecording: false);
        Task serve = WorkerServer.ServeAsync(server, worker, hostCancellation.Token);
        try
        {
            JsonRpcRequest screenshotRequest = JsonRpcCodec.CreateRequest(
                WorkerMethods.Screenshot,
                1,
                request,
                WorkerProtocolJsonContext.Default.ScreenshotRequest);
            await JsonRpcCodec.WriteRequestAsync(client, screenshotRequest, cancellationToken).ConfigureAwait(false);
            JsonRpcResponse? response = await JsonRpcCodec.ReadResponseAsync(client, cancellationToken).ConfigureAwait(false);
            EnsureAcknowledged(response);
        }
        finally
        {
            client.Dispose();
            await hostCancellation.CancelAsync().ConfigureAwait(false);
            await DrainServeAsync(serve).ConfigureAwait(false);
        }
    }

    private static void EnsureAcknowledged(JsonRpcResponse? response)
    {
        if (response is null)
        {
            throw new AppCapException("The worker did not acknowledge the screenshot.");
        }

        if (response.Error is { } error)
        {
            throw new AppCapException(error.Message);
        }

        if (response.Result is not { } result)
        {
            throw new AppCapException("The worker returned an empty screenshot response.");
        }

        ScreenshotResult? acknowledgement = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.ScreenshotResult);
        if (acknowledgement is not { Acknowledged: true })
        {
            throw new AppCapException("The worker did not acknowledge the screenshot.");
        }
    }

    private static async Task DrainServeAsync(Task serve)
    {
        try
        {
            await serve.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
