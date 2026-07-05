using AppCap;
using AppCap.Protocol.Target;
using AppCap.Protocol.Worker;
using System.Collections.Concurrent;

namespace AppCap.Windows;

// The machine-wide worker: a single per-user process, launched just-in-time by a client,
// that multiplexes every recording and screenshot for that user. It owns one
// RecordingSession per recording target, serves the worker protocol over its named pipe
// (RecordingIpc), and self-terminates once it has been idle with no active recordings. All
// worker-protocol methods are keyed by target name so many recordings run concurrently
// without head-of-line blocking.
internal sealed class WorkerHost : IWorkerHost, IDisposable
{
    public const string WorkerCommand = "--appcap-worker";

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, RecordingSession> sessions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource shutdown;
    private long lastActivityTicks = Environment.TickCount64;
    private bool disposed;

    private WorkerHost(CancellationToken cancellationToken) =>
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    public static bool IsWorkerInvocation(IReadOnlyList<string> args) => args.Count > 0 && args[0] == WorkerCommand;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        _ = args;
        try
        {
            using WorkerHost host = new(cancellationToken);
            return await host.RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.OperationalError;
        }
    }

    private async Task<int> RunAsync()
    {
        Task idle = MonitorIdleAsync(shutdown.Token);
        bool owned = await RecordingIpc.RunServerAsync(this, shutdown.Token).ConfigureAwait(false);
        if (!owned)
        {
            // Another worker already owns the pipe; this redundant instance exits quietly.
            await shutdown.CancelAsync().ConfigureAwait(false);
            await ObserveAsync(idle).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        await ObserveAsync(idle).ConfigureAwait(false);
        await StopRemainingSessionsAsync().ConfigureAwait(false);
        return ExitCodes.Success;
    }

    public bool Ping()
    {
        MarkActivity();
        return true;
    }

    public async Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        MarkActivity();

        if (request.TimeLimitSeconds <= 0)
        {
            throw new AppCapException("Recording time limit must be greater than zero.");
        }

        RecordingSession session = new(BuildWindow(request), request.OutputPath, TimeSpan.FromSeconds(request.TimeLimitSeconds), request.IncludeCursor, shutdown.Token);
        if (!sessions.TryAdd(request.TargetName, session))
        {
            session.Dispose();
            throw new AppCapException($"A recording is already running for target '{request.TargetName}'.");
        }

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            await session.StartAsync(linked.Token).ConfigureAwait(false);
        }
        catch
        {
            sessions.TryRemove(new KeyValuePair<string, RecordingSession>(request.TargetName, session));
            session.Dispose();
            MarkActivity();
            throw;
        }

        _ = SuperviseAsync(request.TargetName, session);
        MarkActivity();
    }

    public async Task<bool> StopRecordingAsync(string targetName, bool discard, CancellationToken cancellationToken)
    {
        MarkActivity();
        if (!sessions.TryGetValue(targetName, out RecordingSession? session))
        {
            return false;
        }

        try
        {
            await session.StopAsync(discard, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Remove the finished recording before returning so a client that immediately
            // starts a new recording for the same target is not told one is already running.
            // The supervisor disposes the session once its completion is observed.
            sessions.TryRemove(new KeyValuePair<string, RecordingSession>(targetName, session));
            MarkActivity();
        }

        return true;
    }

    public bool IsRecording(string targetName)
    {
        MarkActivity();
        return sessions.ContainsKey(targetName);
    }

    public Task<bool> AddCaptionAsync(string targetName, string caption, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        cancellationToken.ThrowIfCancellationRequested();
        MarkActivity();
        if (!sessions.TryGetValue(targetName, out RecordingSession? session))
        {
            return Task.FromResult(false);
        }

        session.AddCaption(caption);
        return Task.FromResult(true);
    }

    public async Task<bool> CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        MarkActivity();

        if (!sessions.TryGetValue(request.TargetName, out RecordingSession? session))
        {
            return false;
        }

        CapturedFrame frame = await session.Target.CaptureFrameAsync(request.IncludeCursor, cancellationToken).ConfigureAwait(false);
        await ScreenshotWriter.WriteAsync(frame, request.OutputPath, request.Caption, cancellationToken).ConfigureAwait(false);
        MarkActivity();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        shutdown.Dispose();
    }

    // Watches each recording to completion (whether it was stopped or its window closed) so
    // the session is removed and disposed once it finishes, keeping the multiplexing map in
    // sync and letting the worker go idle when the last recording ends.
    private async Task SuperviseAsync(string targetName, RecordingSession session)
    {
        try
        {
            await session.Completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A save failure is reported to the stopping client; here we only need to clean
            // up the finished session.
        }

        sessions.TryRemove(new KeyValuePair<string, RecordingSession>(targetName, session));
        session.Dispose();
        MarkActivity();
    }

    private async Task MonitorIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(IdleCheckInterval, cancellationToken).ConfigureAwait(false);

                if (!sessions.IsEmpty)
                {
                    continue;
                }

                long idleFor = Environment.TickCount64 - Volatile.Read(ref lastActivityTicks);
                if (idleFor >= (long)IdleTimeout.TotalMilliseconds)
                {
                    await shutdown.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Best-effort save of any recordings still running when the worker is shutting down, so
    // an in-flight recording is finalized rather than lost.
    private async Task StopRemainingSessionsAsync()
    {
        foreach (KeyValuePair<string, RecordingSession> entry in sessions.ToArray())
        {
            try
            {
                await entry.Value.StopAsync(discard: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    private void MarkActivity() => Volatile.Write(ref lastActivityTicks, Environment.TickCount64);

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static TargetWindow BuildWindow(RecordingStartRequest request)
    {
        TargetApplication application = new() { Name = request.ApplicationName, Id = request.ApplicationId };
        return new TargetWindow(application, (nint)request.WindowHandle);
    }
}
