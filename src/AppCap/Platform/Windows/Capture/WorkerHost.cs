using System.Collections.Concurrent;
using AppCap;
using AppCap.Protocol.Target;
using AppCap.Protocol.Worker;

namespace AppCap.Windows;

// The machine-wide worker: a single per-user process launched by the first target attach.
// It multiplexes attached target sessions, recordings, screenshots, and input-device state,
// serves the worker protocol over its named pipe, and stops after the last target detach.
internal sealed class WorkerHost : IWorkerHost, IDisposable
{
    public const string WorkerCommand = "--appcap-worker";

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, RecordingSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RecordingStatusResult> recordingStatuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WorkerTargetSession> targetSessions = new(StringComparer.Ordinal);
    private readonly ITargetResolver targetResolver;
    private readonly IWindowController windowController;
    private readonly IInputInjector inputInjector;
    private readonly IKeyboardInputInjector keyboardInputInjector;
    private readonly CancellationTokenSource shutdown;
    private long lastActivityTicks = Environment.TickCount64;
    private int shutdownAfterResponse;
    private bool disposed;

    private WorkerHost(CancellationToken cancellationToken)
    {
        targetResolver = new TargetResolver(new WindowFinder(), new TargetLauncher());
        windowController = new WindowController();
        inputInjector = new SyntheticPointerInputInjector();
        keyboardInputInjector = new KeyboardInputInjector();
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

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

    public async Task AttachTargetAsync(TargetDescriptorRequest target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        MarkActivity();

        WorkerTargetSession session = CreateTargetSession(target);
        if (!targetSessions.TryAdd(target.TargetName, session))
        {
            session.Dispose();
            throw new AppCapException($"Target '{target.TargetName}' is already attached.", ExitCodes.UsageError);
        }

        Volatile.Write(ref shutdownAfterResponse, 0);
        recordingStatuses[target.TargetName] = new RecordingStatusResult();
        try
        {
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            targetSessions.TryRemove(new KeyValuePair<string, WorkerTargetSession>(target.TargetName, session));
            recordingStatuses.TryRemove(target.TargetName, out _);
            session.Dispose();
            throw;
        }
    }

    public async Task<bool> DetachTargetAsync(string targetName, CancellationToken cancellationToken)
    {
        if (!targetSessions.TryRemove(targetName, out WorkerTargetSession? targetSession))
        {
            return false;
        }

        try
        {
            if (sessions.TryGetValue(targetName, out RecordingSession? recording))
            {
                try
                {
                    await recording.StopAsync(discard: true, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    sessions.TryRemove(new KeyValuePair<string, RecordingSession>(targetName, recording));
                }
            }
        }
        finally
        {
            recordingStatuses.TryRemove(targetName, out _);
            targetSession.Dispose();
            MarkActivity();
        }

        if (targetSessions.IsEmpty)
        {
            Volatile.Write(ref shutdownAfterResponse, 1);
        }

        return true;
    }

    public IReadOnlyList<TargetDescriptorRequest> ListTargets() =>
        targetSessions
            .Select(static entry => new TargetDescriptorRequest
            {
                TargetName = entry.Key,
                ApplicationId = entry.Value.Application.Id,
            })
            .OrderBy(static target => target.TargetName, StringComparer.Ordinal)
            .ToArray();

    public void CompleteRequest()
    {
        if (Volatile.Read(ref shutdownAfterResponse) != 0 && targetSessions.IsEmpty)
        {
            shutdown.Cancel();
        }
    }

    public async Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        MarkActivity();
        WorkerTargetSession targetSession = GetAttachedTargetSession(request.TargetName);
        if (request.TimeLimitSeconds <= 0)
        {
            throw new AppCapException("Recording time limit must be greater than zero.");
        }

        AttachedCaptureSession captureSession = await targetSession.GetCaptureSessionAsync(BuildWindow(request), cancellationToken).ConfigureAwait(false);
        RecordingSession session = new(captureSession, request.OutputPath, TimeSpan.FromSeconds(request.TimeLimitSeconds), request.IncludeCursor, request.Crop, shutdown.Token);
        if (!sessions.TryAdd(request.TargetName, session))
        {
            session.Dispose();
            throw new AppCapException($"A recording is already running for target '{request.TargetName}'.");
        }

        recordingStatuses[request.TargetName] = new RecordingStatusResult
        {
            Recording = true,
            Status = "recording",
            OutputPath = request.OutputPath,
        };

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            await session.StartAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            sessions.TryRemove(new KeyValuePair<string, RecordingSession>(request.TargetName, session));
            session.Dispose();
            recordingStatuses[request.TargetName] = new RecordingStatusResult
            {
                Status = "failed",
                OutputPath = request.OutputPath,
                Error = exception.Message,
            };
            MarkActivity();
            throw;
        }

        _ = SuperviseAsync(request.TargetName, session);
        MarkActivity();
    }

    public async Task<bool> StopRecordingAsync(string targetName, bool discard, CancellationToken cancellationToken)
    {
        MarkActivity();
        _ = GetAttachedTargetSession(targetName);
        if (!sessions.TryGetValue(targetName, out RecordingSession? session))
        {
            return false;
        }

        try
        {
            await session.StopAsync(discard, cancellationToken).ConfigureAwait(false);
            recordingStatuses[targetName] = new RecordingStatusResult
            {
                Status = discard ? "cancelled" : "stopped",
                OutputPath = discard ? null : recordingStatuses.GetValueOrDefault(targetName)?.OutputPath,
            };
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

    public RecordingStatusResult GetRecordingStatus(string targetName)
    {
        MarkActivity();
        _ = GetAttachedTargetSession(targetName);
        RecordingStatusResult current = recordingStatuses.GetValueOrDefault(targetName) ?? new RecordingStatusResult();
        if (current.Recording &&
            sessions.TryGetValue(targetName, out RecordingSession? session) &&
            session.CompletionReason != RecordingCompletionReason.Unknown)
        {
            current = CompletedRecordingStatus(session.CompletionReason, current.OutputPath);
            recordingStatuses[targetName] = current;
        }

        return current;
    }

    public Task<bool> AddCaptionAsync(string targetName, string caption, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        cancellationToken.ThrowIfCancellationRequested();
        MarkActivity();
        _ = GetAttachedTargetSession(targetName);
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

        WorkerTargetSession targetSession = GetAttachedTargetSession(request.TargetName);
        CapturedFrame frame = await targetSession.CaptureFrameAsync(request.IncludeCursor, cancellationToken).ConfigureAwait(false);

        await ScreenshotWriter.WriteAsync(frame, request.OutputPath, request.Caption, request.Crop, cancellationToken).ConfigureAwait(false);
        MarkActivity();
        return true;
    }

    public async Task AttachInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();

        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        await session.AttachInputDeviceAsync(deviceType, cancellationToken).ConfigureAwait(false);
        MarkActivity();
    }

    public async Task RemoveInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();

        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        await session.RemoveInputDeviceAsync(deviceType, cancellationToken).ConfigureAwait(false);
        MarkActivity();
    }

    public async Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetDescriptorRequest target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();

        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        IReadOnlyList<InputDeviceStatus> result = await session.ListInputDevicesAsync(cancellationToken).ConfigureAwait(false);
        MarkActivity();
        return result;
    }

    public async Task TapAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();

        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        await session.TapAsync(x, y, deviceType, cancellationToken).ConfigureAwait(false);
        MarkActivity();
    }

    public async Task MoveMouseAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();
        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        await session.MoveMouseAsync(x, y, deviceType, cancellationToken).ConfigureAwait(false);
        MarkActivity();
    }

    public async Task ClickMouseAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();
        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        await session.ClickMouseAsync(x, y, deviceType, cancellationToken).ConfigureAwait(false);
        MarkActivity();
    }

    public async Task TypeAsync(TargetDescriptorRequest target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        MarkActivity();

        WorkerTargetSession session = GetAttachedTargetSession(target.TargetName);
        await session.TypeAsync(textAndKeys, deviceType, cancellationToken).ConfigureAwait(false);
        MarkActivity();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (KeyValuePair<string, WorkerTargetSession> entry in targetSessions)
        {
            entry.Value.Dispose();
        }

        shutdown.Dispose();
    }

    // Watches each recording to completion so the session is removed and its latest outcome
    // remains available through record status.
    private async Task SuperviseAsync(string targetName, RecordingSession session)
    {
        Exception? failure = null;
        try
        {
            await session.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (recordingStatuses.TryGetValue(targetName, out RecordingStatusResult? current) && current.Recording)
        {
            recordingStatuses[targetName] = failure is null
                ? CompletedRecordingStatus(session.CompletionReason, current.OutputPath)
                : new RecordingStatusResult { Status = "failed", OutputPath = current.OutputPath, Error = failure.Message };
        }

        sessions.TryRemove(new KeyValuePair<string, RecordingSession>(targetName, session));
        session.Dispose();
        MarkActivity();
    }

    private static RecordingStatusResult CompletedRecordingStatus(RecordingCompletionReason reason, string? outputPath) => new()
    {
        Status = reason switch
        {
            RecordingCompletionReason.TimedOut => "timed-out",
            RecordingCompletionReason.AppClosed => "app-closed",
            RecordingCompletionReason.Cancelled => "cancelled",
            _ => "stopped",
        },
        OutputPath = reason == RecordingCompletionReason.Cancelled ? null : outputPath,
    };

    private async Task MonitorIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(IdleCheckInterval, cancellationToken).ConfigureAwait(false);

                if (!sessions.IsEmpty || !targetSessions.IsEmpty)
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

        foreach (KeyValuePair<string, WorkerTargetSession> entry in targetSessions.ToArray())
        {
            if (targetSessions.TryRemove(entry))
            {
                entry.Value.Dispose();
            }
        }
    }

    private void MarkActivity() => Volatile.Write(ref lastActivityTicks, Environment.TickCount64);

    private WorkerTargetSession CreateTargetSession(TargetDescriptorRequest request) =>
        new(
            new TargetApplication { Name = request.TargetName, Id = request.ApplicationId },
            targetResolver,
            windowController,
            inputInjector,
            keyboardInputInjector);

    private WorkerTargetSession GetAttachedTargetSession(string targetName) =>
        targetSessions.TryGetValue(targetName, out WorkerTargetSession? session)
            ? session
            : throw new AppCapException($"Target '{targetName}' is not attached.", ExitCodes.UsageError);

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
