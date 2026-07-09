using AppCap.Protocol.Worker;

namespace AppCap.Tests;

// A controllable IWorkerService used by protocol tests to drive the worker-protocol
// dispatch (recording.status and screenshot) without any real capture or file I/O. It
// records the last screenshot request and can be configured to fail, so tests can verify
// both the acknowledgement and the error paths.
internal sealed class FakeWorkerService : IWorkerService
{
    private readonly string? failWith;

    public FakeWorkerService(bool isRecording, string? failWith = null)
    {
        IsRecording = isRecording;
        this.failWith = failWith;
    }

    public bool IsRecording { get; }

    public ScreenshotRequest? LastScreenshot { get; private set; }

    public Task CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken)
    {
        LastScreenshot = request;
        if (failWith is not null)
        {
            throw new InvalidOperationException(failWith);
        }

        return Task.CompletedTask;
    }
}
