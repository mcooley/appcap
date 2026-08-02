namespace AppCap.Windows;

// Owns recording output while an attached target's graphics capture continues independently.
internal sealed class RecordingSession : IDisposable
{
    private readonly AttachedCaptureSession captureSession;
    private readonly RecordingWriter writer;
    private readonly ProcessLoopbackAudioCapture? audioCapture;
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenSource timeLimitCancellation = new();
    private readonly TaskCompletionSource finalizationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan timeLimit;
    private readonly bool includeCursor;
    private Task completion = Task.CompletedTask;
    private int stopRequested;
    private bool disposed;
    private int completionReason;

    public RecordingSession(
        AttachedCaptureSession captureSession,
        string outputPath,
        TimeSpan timeLimit,
        bool includeCursor,
        bool includeAudio,
        int? processId,
        CropRectangle? crop,
        CancellationToken cancellationToken)
    {
        this.captureSession = captureSession;
        this.timeLimit = timeLimit;
        this.includeCursor = includeCursor;
        this.cancellationToken = cancellationToken;
        writer = new RecordingWriter(outputPath, crop, includeAudio);
        if (includeAudio)
        {
            audioCapture = new ProcessLoopbackAudioCapture(processId ?? throw new ArgumentNullException(nameof(processId)));
        }
    }

    public Task Completion => completion;

    public RecordingCompletionReason CompletionReason => (RecordingCompletionReason)Volatile.Read(ref completionReason);

    public void AddCaption(string text) => writer.AddCaption(text);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        captureSession.AttachWriter(writer, includeCursor);
        (int width, int height) = captureSession.RefreshSize();
        Task writerStart = writer.StartAsync(width, height, this.cancellationToken);
        try
        {
            if (audioCapture is not null)
            {
                await audioCapture.StartAsync(writer.AddAudioPacket, this.cancellationToken).ConfigureAwait(false);
            }

            await writerStart.ConfigureAwait(false);
        }
        catch
        {
            captureSession.DetachWriter(writer);
            try
            {
                if (audioCapture is not null)
                {
                    await audioCapture.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }

            writer.CompleteAudio();
            try
            {
                await writer.StopAsync(discard: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

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
            await FinalizeAsync(discard, cancellationToken).ConfigureAwait(false);
        }

        await finalizationCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        audioCapture?.Dispose();
        writer.Dispose();
    }

    private async Task CompleteAsync()
    {
        List<Task> completionSources = [captureSession.Completion, writer.Completion];
        if (audioCapture is not null)
        {
            completionSources.Add(audioCapture.Completion);
        }

        Task trigger = await Task.WhenAny(completionSources).ConfigureAwait(false);
        Exception? triggerFailure = null;
        try
        {
            await trigger.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            triggerFailure = exception;
        }

        if (Interlocked.CompareExchange(ref stopRequested, 1, 0) == 0)
        {
            RecordingCompletionReason reason = ReferenceEquals(trigger, captureSession.Completion) && triggerFailure is null
                ? RecordingCompletionReason.AppClosed
                : RecordingCompletionReason.Unknown;
            Interlocked.CompareExchange(ref completionReason, (int)reason, (int)RecordingCompletionReason.Unknown);
            CancelTimeLimit();
            await FinalizeAsync(discard: triggerFailure is not null, CancellationToken.None).ConfigureAwait(false);
        }

        await finalizationCompleted.Task.ConfigureAwait(false);
        if (triggerFailure is not null)
        {
            throw triggerFailure;
        }

        try
        {
            await writer.Completion.ConfigureAwait(false);
            if (audioCapture is not null)
            {
                await audioCapture.Completion.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (CompletionReason == RecordingCompletionReason.Cancelled)
        {
        }
    }

    private async Task FinalizeAsync(bool discard, CancellationToken cancellationToken)
    {
        Exception? producerFailure = null;
        captureSession.DetachWriter(writer);
        try
        {
            if (audioCapture is not null)
            {
                await audioCapture.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            producerFailure = exception;
            discard = true;
        }
        finally
        {
            writer.CompleteAudio();
        }

        try
        {
            await writer.StopAsync(discard, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            finalizationCompleted.TrySetResult();
        }

        if (producerFailure is not null)
        {
            throw producerFailure;
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
