using System.Diagnostics;
using global::Windows.Win32;

namespace AppCap.Windows;

internal static class WorkerProcessController
{
    private static readonly TimeSpan WorkerLaunchTimeout = TimeSpan.FromSeconds(10);

    public static async Task EnsureWorkerRunningAsync(CancellationToken cancellationToken)
    {
        int? processId = await RecordingIpc.GetWorkerProcessIdAsync(cancellationToken).ConfigureAwait(false);
        if (processId is not null)
        {
            AllowWorkerToSetForegroundWindow(processId.Value);
            return;
        }

        using WorkerLaunchLock? launchLock = await RecordingIpc.TryAcquireLaunchLockAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (launchLock is null)
        {
            throw new AppCapException("Timed out waiting to launch the worker.");
        }

        processId = await RecordingIpc.GetWorkerProcessIdAsync(cancellationToken).ConfigureAwait(false);
        if (processId is not null)
        {
            AllowWorkerToSetForegroundWindow(processId.Value);
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
            AllowWorkerToSetForegroundWindow(process.Id);
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
            int? processId = await RecordingIpc.GetWorkerProcessIdAsync(timeoutSource.Token).ConfigureAwait(false);
            if (processId is not null)
            {
                AllowWorkerToSetForegroundWindow(processId.Value);
                return;
            }

            await Task.Delay(100, timeoutSource.Token).ConfigureAwait(false);
        }

        throw new AppCapException("The worker did not start.");
    }

    private static void AllowWorkerToSetForegroundWindow(int processId) =>
        _ = PInvoke.AllowSetForegroundWindow((uint)processId);
}
