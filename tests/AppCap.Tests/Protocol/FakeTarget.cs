using AppCap.Protocol.Target;

namespace AppCap.Tests;

// A controllable ITarget used by protocol tests to drive the target-protocol dispatch
// (status and capture_frame) without any real graphics capture.
internal sealed class FakeTarget : ITarget
{
    private readonly CapturedFrame frame;

    public FakeTarget(CapturedFrame? frame = null)
    {
        this.frame = frame ?? new CapturedFrame(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
    }

    public bool? LastIncludeCursor { get; private set; }

    public Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        LastIncludeCursor = includeCursor;
        return Task.FromResult(frame);
    }
}
