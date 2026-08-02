using AppCap;
using AppCap.Protocol.Worker;
namespace AppCap.Windows;

// Client-side recording orchestration. Recording work is owned by the machine-wide worker
// process (one per user, multiplexing every attached target). Target attachment owns worker
// startup; this controller only drives an existing worker over the worker protocol.
// It resolves the target window on the client side (a window handle is valid across
// processes on the same desktop) and passes the descriptor to the worker.
public sealed class RecordingController : IRecordingController
{
    public async Task StartAsync(TargetWindow window, string outputPath, TimeSpan timeLimit, bool includeCursor, bool includeAudio, CropRectangle? crop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (timeLimit <= TimeSpan.Zero || timeLimit.TotalSeconds > int.MaxValue)
        {
            throw new AppCapException("Recording time limit must be between 1 second and 24,855 days.", ExitCodes.UsageError);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        RecordingStartRequest request = new()
        {
            TargetName = window.Application.Name,
            ApplicationName = window.Application.Name,
            ApplicationId = window.Application.Id,
            WindowHandle = window.Handle,
            OutputPath = fullOutputPath,
            TimeLimitSeconds = checked((int)timeLimit.TotalSeconds),
            IncludeCursor = includeCursor,
            IncludeAudio = includeAudio,
            Crop = crop,
        };

        // The worker serializes concurrent starts for the same target and answers with a
        // clear error if one is already running, so no client-side start lock is needed.
        await RecordingIpc.StartRecordingAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await RecordingIpc.SendStopAsync(target.Name, cancellationToken).ConfigureAwait(false))
        {
            throw new AppCapException($"No recording is running for target '{TargetFormatter.Format(target)}'.");
        }
    }

    public async Task CancelAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await RecordingIpc.SendCancelAsync(target.Name, cancellationToken).ConfigureAwait(false))
        {
            throw new AppCapException($"No recording is running for target '{TargetFormatter.Format(target)}'.");
        }
    }

    public async Task AddCaptionAsync(TargetApplication target, string caption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await RecordingIpc.SendCaptionAsync(target.Name, caption, cancellationToken).ConfigureAwait(false))
        {
            throw new AppCapException($"No recording is running for target '{TargetFormatter.Format(target)}'.");
        }
    }

    public async Task<RecordingStatus> GetStatusAsync(TargetApplication target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        RecordingStatusResult status = await RecordingIpc.GetRecordingStatusAsync(target.Name, cancellationToken).ConfigureAwait(false);
        return new RecordingStatus(status.Status, status.OutputPath, status.Error);
    }

}
