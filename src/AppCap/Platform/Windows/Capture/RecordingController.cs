using AppCap;
using AppCap.Protocol.Worker;
using System.Diagnostics;

namespace AppCap.Windows;

// Client-side recording orchestration. Recording work is owned by the machine-wide worker
// process (one per user, multiplexing every target); this controller ensures that worker is
// running — launching it just-in-time if not — and then drives it over the worker protocol.
// It resolves the target window on the client side (a window handle is valid across
// processes on the same desktop) and passes the descriptor to the worker.
public sealed class RecordingController : IRecordingController
{
    private static readonly TimeSpan WorkerLaunchTimeout = TimeSpan.FromSeconds(10);

    public async Task StartAsync(TargetWindow window, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await EnsureWorkerRunningAsync(cancellationToken).ConfigureAwait(false);

        RecordingStartRequest request = new()
        {
            TargetName = window.Target.Name,
            ApplicationName = window.Application.Name,
            ApplicationId = window.Application.Id,
            WindowHandle = window.Handle,
            OutputPath = fullOutputPath,
        };

        // The worker serializes concurrent starts for the same target and answers with a
        // clear error if one is already running, so no client-side start lock is needed.
        await RecordingIpc.StartRecordingAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await RecordingIpc.SendStopAsync(target.Name, cancellationToken).ConfigureAwait(false))
        {
            throw new AppCapException($"No recording is running for target '{TargetFormatter.Format(target)}'.");
        }
    }

    public async Task CancelAsync(TargetConfiguration target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await RecordingIpc.SendCancelAsync(target.Name, cancellationToken).ConfigureAwait(false))
        {
            throw new AppCapException($"No recording is running for target '{TargetFormatter.Format(target)}'.");
        }
    }

    // Ensures a machine worker is reachable, launching one just-in-time if not. A
    // cross-process lock serializes launches so two clients that both find no worker cannot
    // spawn competing workers; the winner launches the worker and the others reuse it.
    private static async Task EnsureWorkerRunningAsync(CancellationToken cancellationToken)
    {
        if (await RecordingIpc.PingAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        using WorkerLaunchLock? launchLock = await RecordingIpc.TryAcquireLaunchLockAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (launchLock is null)
        {
            throw new AppCapException("Timed out waiting to launch the recording worker.");
        }

        // Another client may have launched the worker while we waited for the lock.
        if (await RecordingIpc.PingAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        LaunchWorker();
        await WaitForWorkerAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void LaunchWorker()
    {
        string executablePath = Environment.ProcessPath ?? throw new AppCapException("Recording worker could not be launched.");
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(WorkerHost.WorkerCommand);

        try
        {
            // The worker is an independent, long-lived process: it must outlive this client,
            // so its handle is disposed without terminating it.
            Process process = Process.Start(startInfo) ?? throw new AppCapException("Recording worker could not be launched.");
            process.Dispose();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new AppCapException("Recording worker could not be launched.", exception);
        }
    }

    private static async Task WaitForWorkerAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(WorkerLaunchTimeout);
        while (!timeoutSource.IsCancellationRequested)
        {
            if (await RecordingIpc.PingAsync(timeoutSource.Token).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100, timeoutSource.Token).ConfigureAwait(false);
        }

        throw new AppCapException("Recording worker did not start.");
    }
}
