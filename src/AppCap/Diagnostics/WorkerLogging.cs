using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AppCap.Diagnostics;

internal static partial class WorkerLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Worker started. PID: {ProcessId}; log: {LogPath}")]
    public static partial void Started(ILogger logger, int processId, string logPath);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Worker terminated unexpectedly.")]
    public static partial void TerminatedUnexpectedly(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Worker owns the named pipe.")]
    public static partial void PipeOwned(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Worker exited because another worker owns the named pipe.")]
    public static partial void RedundantWorkerExited(ILogger logger);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Target attached. Target: {TargetName}; application: {ApplicationId}")]
    public static partial void TargetAttached(ILogger logger, string targetName, string applicationId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Target detached. Target: {TargetName}")]
    public static partial void TargetDetached(ILogger logger, string targetName);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Recording started. Target: {TargetName}; window: 0x{WindowHandle:X}; output: {OutputPath}; audio: {IncludeAudio}; cursor: {IncludeCursor}; time limit seconds: {TimeLimitSeconds}")]
    public static partial void RecordingStarted(ILogger logger, string targetName, long windowHandle, string outputPath, bool includeAudio, bool includeCursor, double timeLimitSeconds);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Recording completed. Target: {TargetName}; reason: {Reason}; discard: {Discard}; output: {OutputPath}")]
    public static partial void RecordingCompleted(ILogger logger, string targetName, string reason, bool discard, string? outputPath);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Error, Message = "Recording failed. Target: {TargetName}; output: {OutputPath}")]
    public static partial void RecordingFailed(ILogger logger, string targetName, string outputPath, Exception exception);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "Could not finalize a recording during worker shutdown. Target: {TargetName}")]
    public static partial void ShutdownFinalizationFailed(ILogger logger, string targetName, Exception exception);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Worker idle timeout elapsed; shutting down.")]
    public static partial void IdleShutdown(ILogger logger);
}

internal static partial class TargetLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Resolving target window. Target: {TargetName}; application: {ApplicationId}")]
    public static partial void ResolveStarted(ILogger logger, string targetName, string applicationId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Resolved target window. Target: {TargetName}; handle: 0x{WindowHandle:X}")]
    public static partial void ResolveSucceeded(ILogger logger, string targetName, nint windowHandle);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Launching target while resolving its window. Target: {TargetName}; application: {ApplicationId}")]
    public static partial void LaunchingTarget(ILogger logger, string targetName, string applicationId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Timed out waiting for target window. Target: {TargetName}; application: {ApplicationId}")]
    public static partial void ResolveTimedOut(ILogger logger, string targetName, string applicationId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Attached target is not running. Target: {TargetName}; application: {ApplicationId}")]
    public static partial void TargetNotRunning(ILogger logger, string targetName, string applicationId);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Attached-target capture monitor failed. Target: {TargetName}")]
    public static partial void CaptureMonitorFailed(ILogger logger, string targetName, Exception exception);
}

internal static partial class CaptureLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Capture started. Target: {TargetName}; window: 0x{WindowHandle:X}; dimensions: {Width}x{Height}")]
    public static partial void Started(ILogger logger, string targetName, nint windowHandle, int width, int height);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Capture received its first frame. Target: {TargetName}; dimensions: {Width}x{Height}")]
    public static partial void FirstFrame(ILogger logger, string targetName, int width, int height);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Capture dimensions changed. Target: {TargetName}; dimensions: {Width}x{Height}")]
    public static partial void DimensionsChanged(ILogger logger, string targetName, int width, int height);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Capture ended because the target window closed. Target: {TargetName}")]
    public static partial void TargetWindowClosed(ILogger logger, string targetName);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "Capture failed. Target: {TargetName}")]
    public static partial void Failed(ILogger logger, string targetName, Exception exception);
}

internal static partial class RecordingLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Recording encoder started. Output: {OutputPath}; source dimensions: {SourceWidth}x{SourceHeight}; output dimensions: {OutputWidth}x{OutputHeight}; audio: {IncludeAudio}")]
    public static partial void EncoderStarted(ILogger logger, string outputPath, int sourceWidth, int sourceHeight, int outputWidth, int outputHeight, bool includeAudio);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Recording output finalized. Output: {OutputPath}; bytes: {Length}")]
    public static partial void OutputFinalized(ILogger logger, string outputPath, long length);
}

internal sealed class WorkerLogSession : IDisposable
{
    private readonly ILoggerFactory loggerFactory;

    private WorkerLogSession(ILoggerFactory loggerFactory, string logPath)
    {
        this.loggerFactory = loggerFactory;
        LogPath = logPath;
    }

    public string LogPath { get; }

    public ILogger CreateLogger<T>() => loggerFactory.CreateLogger<T>();

    public static WorkerLogSession? TryCreate(TextWriter errorOutput)
    {
        ArgumentNullException.ThrowIfNull(errorOutput);

        try
        {
            RollingFileLoggerProvider provider = new(WorkerLogPaths.GetDirectory());
            ILoggerFactory factory = LoggerFactory.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Information).AddProvider(provider));
            return new WorkerLogSession(factory, provider.ActiveLogPath);
        }
        catch (Exception exception)
        {
            errorOutput.WriteLine($"AppCap worker logging is disabled: {exception.Message}");
            return null;
        }
    }

    public void Dispose() => loggerFactory.Dispose();
}

internal static class WorkerLogPaths
{
    public static string GetDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppCap",
        "Logs");
}

internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const long MaximumSegmentBytes = 10L * 1024 * 1024;
    private const long MaximumDirectoryBytes = 100L * 1024 * 1024;
    private const long MaximumClosedSegmentBytes = MaximumDirectoryBytes - MaximumSegmentBytes;
    private readonly Channel<string> entries = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly Task writerTask;
    private readonly string directory;
    private readonly string prefix;
    private int segment;
    private bool disposed;

    public RollingFileLoggerProvider(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        prefix = $"worker-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss.fffffffZ}-{Environment.ProcessId}";
        ActiveLogPath = GetSegmentPath();
        PruneClosedSegments();
        writerTask = Task.Run(WriteAsync);
    }

    public string ActiveLogPath { get; private set; }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(categoryName, this);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        entries.Writer.TryComplete();
        try
        {
            writerTask.GetAwaiter().GetResult();
        }
        catch
        {
            // Diagnostics must not change worker shutdown behavior.
        }
    }

    public void Write(string entry)
    {
        if (!disposed)
        {
            _ = entries.Writer.TryWrite(entry);
        }
    }

    private async Task WriteAsync()
    {
        FileStream? stream = null;
        StreamWriter? writer = null;
        long written = 0;
        try
        {
            await foreach (string entry in entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (writer is null || written + entry.Length + Environment.NewLine.Length > MaximumSegmentBytes)
                    {
                        writer?.Dispose();
                        stream?.Dispose();
                        if (writer is not null)
                        {
                            segment++;
                            ActiveLogPath = GetSegmentPath();
                            PruneClosedSegments();
                        }

                        stream = new FileStream(ActiveLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                        writer = new StreamWriter(stream) { AutoFlush = true };
                        written = stream.Length;
                    }

                    await writer.WriteLineAsync(entry).ConfigureAwait(false);
                    written += entry.Length + Environment.NewLine.Length;
                }
                catch
                {
                    writer?.Dispose();
                    stream?.Dispose();
                    writer = null;
                    stream = null;
                }
            }
        }
        finally
        {
            writer?.Dispose();
            stream?.Dispose();
        }
    }

    private string GetSegmentPath() => Path.Combine(directory, $"{prefix}-{segment:D3}.log");

    private void PruneClosedSegments()
    {
        FileInfo[] files = new DirectoryInfo(directory)
            .EnumerateFiles("worker-*.log")
            .Where(file => !file.FullName.Equals(ActiveLogPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.CreationTimeUtc)
            .ToArray();
        long totalBytes = files.Sum(file => file.Length);
        foreach (FileInfo file in files)
        {
            if (totalBytes <= MaximumClosedSegmentBytes)
            {
                break;
            }

            totalBytes -= file.Length;
            file.Delete();
        }
    }
}

internal sealed class RollingFileLogger(string categoryName, RollingFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string exceptionText = exception is null
            ? string.Empty
            : $" exception={Escape(exception.ToString())}";
        provider.Write($"{DateTimeOffset.UtcNow:O} level={logLevel} eventId={eventId.Id} category={categoryName} pid={Environment.ProcessId} thread={Environment.CurrentManagedThreadId} message={Escape(formatter(state, exception))}{exceptionText}");
    }

    private static string Escape(string value) => value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}