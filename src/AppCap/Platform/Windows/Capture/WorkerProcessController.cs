using System.Diagnostics;

namespace AppCap.Windows;

internal static class WorkerProcessController
{
    private static readonly TimeSpan WorkerLaunchTimeout = TimeSpan.FromSeconds(10);

    public static async Task EnsureWorkerRunningAsync(CancellationToken cancellationToken)
    {
        if (await RecordingIpc.PingAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        using WorkerLaunchLock? launchLock = await RecordingIpc.TryAcquireLaunchLockAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (launchLock is null)
        {
            throw new AppCapException("Timed out waiting to launch the worker.");
        }

        if (await RecordingIpc.PingAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        LaunchWorker();
        await WaitForWorkerAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void LaunchWorker()
    {
        string executablePath = Environment.ProcessPath ?? throw new AppCapException("The worker could not be launched.");
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(WorkerHost.WorkerCommand);

        try
        {
            Process process = Process.Start(startInfo) ?? throw new AppCapException("The worker could not be launched.");
            process.Dispose();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new AppCapException("The worker could not be launched.", exception);
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

        throw new AppCapException("The worker did not start.");
    }
}
