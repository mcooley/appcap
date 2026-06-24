using RunMc;
using System.Diagnostics;
using System.Globalization;

namespace RunMc.Windows;

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

        string executablePath = Environment.ProcessPath ?? throw new RunMcException("Recording worker could not be launched.");
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        AddWorkerArguments(startInfo, window, fullOutputPath);

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new RunMcException("Recording worker could not be launched.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new RunMcException("Recording worker could not be launched.", exception);
        }

        await WaitForWorkerAsync(window.Target.Name, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await RecordingIpc.SendStopAsync(target.Name, cancellationToken).ConfigureAwait(false))
        {
            throw new RunMcException($"No recording is running for target '{TargetFormatter.Format(target)}'.");
        }
    }

    private static void AddWorkerArguments(ProcessStartInfo startInfo, TargetWindow window, string outputPath)
    {
        startInfo.ArgumentList.Add(RecordingWorker.WorkerCommand);
        startInfo.ArgumentList.Add("--target-name");
        startInfo.ArgumentList.Add(window.Target.Name);
        startInfo.ArgumentList.Add("--application-name");
        startInfo.ArgumentList.Add(window.Application.Name);
        startInfo.ArgumentList.Add("--package-family-name");
        startInfo.ArgumentList.Add(window.Application.PackageFamilyName);
        startInfo.ArgumentList.Add("--aumid");
        startInfo.ArgumentList.Add(window.Application.Aumid);
        startInfo.ArgumentList.Add("--window-handle");
        startInfo.ArgumentList.Add(window.Handle.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
    }

    private static async Task WaitForWorkerAsync(string targetName, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));
        while (!timeoutSource.IsCancellationRequested)
        {
            if (await RecordingIpc.IsRecordingAsync(targetName, timeoutSource.Token).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100, timeoutSource.Token).ConfigureAwait(false);
        }

        throw new RunMcException("Recording worker did not start.");
    }
}