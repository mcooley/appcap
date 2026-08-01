namespace AppCap.Windows;

// Owns recording output while an attached target's graphics capture continues independently.
internal sealed class RecordingSession : IDisposable
{
    private readonly AttachedCaptureSession captureSession;
    private readonly RecordingWriter writer;
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenSource timeLimitCancellation = new();
    private readonly TaskCompletionSource finalizationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan timeLimit;
    private readonly bool includeCursor;
    private Task completion = Task.CompletedTask;
    private int stopRequested;
    private bool disposed;
    private int completionReason;

    public RecordingSession(AttachedCaptureSession captureSession, string outputPath, TimeSpan timeLimit, bool includeCursor, CropRectangle? crop, CancellationToken cancellationToken)
    {
        this.captureSession = captureSession;
        this.timeLimit = timeLimit;
        this.includeCursor = includeCursor;
        this.cancellationToken = cancellationToken;
        writer = new RecordingWriter(outputPath, crop);
    }

    public Task Completion => completion;

    public RecordingCompletionReason CompletionReason => (RecordingCompletionReason)Volatile.Read(ref completionReason);

    public void AddCaption(string text) => writer.AddCaption(text);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        captureSession.AttachWriter(writer, includeCursor);
        try
        {
            await writer.StartAsync(captureSession.Width, captureSession.Height, this.cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            captureSession.DetachWriter(writer);
            throw;
        }

        completion = CompleteAsync();
        _ = StopAtTimeLimitAsync();
    }

    public Task<bool> StopAsync(bool discard, CancellationToken cancellationToken) =>
        StopAsync(discard, discard ? RecordingCompletionReason.Cancelled : RecordingCompletionReason.Stopped, cancellationToken);

    public async Task<bool> StopAsync(bool discard, RecordingCompletionReason reason, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref stopRequested, 1, 0) == 0)
        {
            Volatile.Write(ref completionReason, (int)reason);
            CancelTimeLimit();
            captureSession.DetachWriter(writer);
        }

        try
        {
            await writer.StopAsync(discard, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            finalizationCompleted.TrySetResult();
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelTimeLimit();
        timeLimitCancellation.Dispose();
        writer.Dispose();
    }

    private async Task CompleteAsync()
    {
        Exception? failure = null;
        try
        {
            await writer.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (CompletionReason == RecordingCompletionReason.Cancelled)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (Interlocked.CompareExchange(ref stopRequested, 1, 0) == 0)
        {
            Interlocked.CompareExchange(ref completionReason, (int)RecordingCompletionReason.AppClosed, (int)RecordingCompletionReason.Unknown);
            try
            {
                await writer.StopAsync(discard: false, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                finalizationCompleted.TrySetResult();
            }
        }

        await finalizationCompleted.Task.ConfigureAwait(false);
        if (failure is not null)
        {
            throw failure;
        }
    }

    private async Task StopAtTimeLimitAsync()
    {
        try
        {
            await Task.Delay(timeLimit, timeLimitCancellation.Token).ConfigureAwait(false);
            await StopAsync(discard: false, RecordingCompletionReason.TimedOut, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeLimitCancellation.IsCancellationRequested)
        {
        }
    }

    private void CancelTimeLimit()
    {
        try
        {
            timeLimitCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

internal enum RecordingCompletionReason
{
    Unknown,
    Stopped,
    Cancelled,
    TimedOut,
    AppClosed,
}
