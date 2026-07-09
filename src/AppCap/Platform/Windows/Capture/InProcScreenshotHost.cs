using AppCap.Protocol.Target;
using AppCap.Protocol.Worker;

namespace AppCap.Windows;

// Worker-protocol host used for the in-proc screenshot path: when nothing is recording for
// a target, the client hosts this worker in its own process over an in-proc transport and
// asks it for a single screenshot. It composes a target (which produces raw frames) with
// the worker's file/render responsibilities — capturing a frame, rendering any caption, and
// writing the PNG — so the non-recording path uses the very same protocol, codec, and
// dispatch as the recording path; only the transport differs. It does not manage recordings.
internal sealed class InProcScreenshotHost : IWorkerHost
{
    private readonly ITarget target;

    public InProcScreenshotHost(ITarget target) => this.target = target;

    public bool Ping() => true;

    public Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken) =>
        throw new AppCapException("The in-process screenshot worker does not record.");

    public Task<bool> StopRecordingAsync(string targetName, bool discard, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public bool IsRecording(string targetName) => false;

    public async Task<bool> CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CapturedFrame frame = await target.CaptureFrameAsync(request.IncludeCursor, cancellationToken).ConfigureAwait(false);
        await ScreenshotWriter.WriteAsync(frame, request.OutputPath, request.Caption, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
