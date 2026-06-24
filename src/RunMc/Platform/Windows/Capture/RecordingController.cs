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

        string pipeName = RecordingIpc.GetPipeName(window.Target.Name);
        if (await RecordingIpc.IsRecordingAsync(pipeName, cancellationToken).ConfigureAwait(false))
        {
            throw new RunMcException($"A recording is already running for target '{TargetFormatter.Format(window.Target)}'.");
        }

        string executablePath = Environment.ProcessPath ?? throw new RunMcException("Recording worker could not be launched.");
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        AddWorkerArguments(startInfo, window, fullOutputPath, pipeName);

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new RunMcException("Recording worker could not be launched.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new RunMcException("Recording worker could not be launched.", exception);
        }

        await WaitForWorkerAsync(pipeName, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        string response = await RecordingIpc.SendStopAsync(RecordingIpc.GetPipeName(target.Name), cancellationToken).ConfigureAwait(false);
        if (!string.Equals(response, RecordingIpc.OkResponse, StringComparison.Ordinal))
        {
            throw new RunMcException(string.IsNullOrWhiteSpace(response) ? $"No recording is running for target '{TargetFormatter.Format(target)}'." : response);
        }
    }

    private static void AddWorkerArguments(ProcessStartInfo startInfo, TargetWindow window, string outputPath, string pipeName)
    {
        startInfo.ArgumentList.Add(RecordingIpc.WorkerCommand);
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
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
    }

    private static async Task WaitForWorkerAsync(string pipeName, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));
        while (!timeoutSource.IsCancellationRequested)
        {
            if (await RecordingIpc.IsRecordingAsync(pipeName, timeoutSource.Token).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100, timeoutSource.Token).ConfigureAwait(false);
        }

        throw new RunMcException("Recording worker did not start.");
    }
}