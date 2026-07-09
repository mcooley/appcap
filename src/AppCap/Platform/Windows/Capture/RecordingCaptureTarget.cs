using AppCap;
using AppCap.Protocol.Target;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;

namespace AppCap.Windows;

// Target that serves frames from a recording's live capture session, used by the
// recording worker so a screenshot taken while recording never starts a second capture
// session. It reads the pixels of the next frame the recording produces. Reading on the
// capture callback thread avoids racing the encoder for the shared Direct3D context.
internal sealed class RecordingCaptureTarget : ITarget
{
    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(10);

    private readonly TargetWindow window;
    private readonly object gate = new();
    private TaskCompletionSource<CapturedFrame>? pending;

    public RecordingCaptureTarget(TargetWindow window) => this.window = window;

    public async Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<CapturedFrame> request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            // Supersede any earlier request that never received a frame so the newest
            // caller is the one served by the next frame that arrives.
            pending?.TrySetCanceled(CancellationToken.None);
            pending = request;
        }

        // A recording only produces frames when the window changes, so nudge the window
        // to repaint. This guarantees a frame arrives even for an otherwise idle window.
        RequestFrame();

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ScreenshotTimeout);
        using CancellationTokenRegistration registration = timeoutSource.Token.Register(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                request.TrySetCanceled(cancellationToken);
            }
            else
            {
                request.TrySetException(new AppCapException("Screenshot capture timed out while recording."));
            }
        });

        try
        {
            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(pending, request))
                {
                    pending = null;
                }
            }
        }
    }

    // Offers a freshly captured recording frame to a waiting screenshot request. Called
    // on the capture callback thread before the frame is handed to the encoder, so the
    // pixels are read while the frame is still owned by the capture pipeline.
    public void OfferFrame(Direct3D11CaptureFrame frame)
    {
        TaskCompletionSource<CapturedFrame>? request;
        lock (gate)
        {
            request = pending;
            pending = null;
        }

        if (request is null)
        {
            return;
        }

        try
        {
            string? capturedFrom = ScreenshotMetadata.TryCreate(window)?.CapturedFrom;
            CapturedFrame captured = FramePixels.ReadAsync(frame.Surface, capturedFrom, CancellationToken.None).GetAwaiter().GetResult();
            request.TrySetResult(captured);
        }
        catch (Exception exception)
        {
            request.TrySetException(exception);
        }
    }

    public void RequestFrame()
    {
        _ = PInvoke.RedrawWindow(
            new HWND(window.Handle),
            lprcUpdate: null,
            hrgnUpdate: null,
            REDRAW_WINDOW_FLAGS.RDW_INVALIDATE | REDRAW_WINDOW_FLAGS.RDW_UPDATENOW | REDRAW_WINDOW_FLAGS.RDW_ALLCHILDREN);
    }
}
