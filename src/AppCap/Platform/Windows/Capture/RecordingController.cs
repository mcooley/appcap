using AppCap;
using System.Diagnostics;
using System.Globalization;

namespace AppCap.Windows;

public sealed class RecordingController : IRecordingController
{
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

        // Hold the start lock until the worker is confirmed running so two
        // concurrent starts for the same target cannot both spawn competing workers.
        using RecordingStartLock startLock = await RecordingIpc.BeginStartAsync(window.Target.Name, cancellationToken).ConfigureAwait(false);

        string executablePath = Environment.ProcessPath ?? throw new AppCapException("Recording worker could not be launched.");
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        AddWorkerArguments(startInfo, window, fullOutputPath);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new AppCapException("Recording worker could not be launched.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new AppCapException("Recording worker could not be launched.", exception);
        }

        try
        {
            await WaitForWorkerAsync(window.Target.Name, process, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TerminateWorkerAsync(window.Target.Name, process).ConfigureAwait(false);
            throw;
        }
        finally
        {
            process.Dispose();
        }
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

    private static void AddWorkerArguments(ProcessStartInfo startInfo, TargetWindow window, string outputPath)
    {
        startInfo.ArgumentList.Add(RecordingWorker.WorkerCommand);
        startInfo.ArgumentList.Add("--target-name");
        startInfo.ArgumentList.Add(window.Target.Name);
        startInfo.ArgumentList.Add("--application-name");
        startInfo.ArgumentList.Add(window.Application.Name);
        startInfo.ArgumentList.Add("--aumid");
        startInfo.ArgumentList.Add(window.Application.Id);
        startInfo.ArgumentList.Add("--window-handle");
        startInfo.ArgumentList.Add(window.Handle.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
    }

    private static async Task WaitForWorkerAsync(string targetName, Process process, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));
        while (!timeoutSource.IsCancellationRequested)
        {
            // If the worker exits before confirming it is recording, it failed to start;
            // surface its reported reason and exit code rather than a generic timeout.
            if (process.HasExited)
            {
                throw await CreateWorkerFailureAsync(process).ConfigureAwait(false);
            }

            if (await RecordingIpc.IsRecordingAsync(targetName, timeoutSource.Token).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100, timeoutSource.Token).ConfigureAwait(false);
        }

        throw new AppCapException("Recording worker did not start.");
    }

    // Translates a worker that exited before it started recording into a structured
    // failure: its stderr becomes the message and its process exit code is mapped into
    // the CLI exit-code scheme, so callers see the real reason instead of a timeout.
    private static async Task<AppCapException> CreateWorkerFailureAsync(Process process)
    {
        string error = (await process.StandardError.ReadToEndAsync().ConfigureAwait(false)).Trim();
        int exitCode = process.ExitCode;
        string message = error.Length > 0
            ? error
            : $"Recording worker exited with code {exitCode.ToString(CultureInfo.InvariantCulture)} before it started recording.";
        return new AppCapException(message, MapWorkerExitCode(exitCode));
    }

    // A usage error from the worker stays a usage error; every other non-success exit
    // (including native crash codes) is reported as an operational failure.
    private static int MapWorkerExitCode(int workerExitCode)
        => workerExitCode == ExitCodes.UsageError ? ExitCodes.UsageError : ExitCodes.OperationalError;

    // Cleans up a worker that failed to confirm it started. If the worker has not
    // already exited, it is asked to cancel (discarding any partial output) and then
    // forcibly killed if it does not exit promptly, so no orphaned worker is left
    // recording in the background.
    private static async Task TerminateWorkerAsync(string targetName, Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            await RecordingIpc.SendCancelAsync(targetName, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AppCapException)
        {
        }

        try
        {
            using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}