using AppCap.Protocol.Target;
using AppCap.Protocol.Worker;

namespace AppCap.Windows;

// Worker-side implementation of the worker protocol's screenshot capability. Composes a
// target (which produces raw frames) with the worker's file/render responsibilities:
// it captures a frame from the target, renders any caption, and writes the PNG. In-proc
// the target is called directly (the optimized path); the same code would drive a remote
// target through the target protocol without change.
internal sealed class WorkerService : IWorkerService
{
    private readonly ITarget target;

    public WorkerService(ITarget target, bool isRecording)
    {
        this.target = target;
        IsRecording = isRecording;
    }

    public bool IsRecording { get; }

    public async Task CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CapturedFrame frame = await target.CaptureFrameAsync(request.IncludeCursor, cancellationToken).ConfigureAwait(false);
        await ScreenshotWriter.WriteAsync(frame, request.OutputPath, request.Caption, cancellationToken).ConfigureAwait(false);
    }
}
