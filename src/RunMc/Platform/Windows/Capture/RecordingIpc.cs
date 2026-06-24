using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace RunMc.Windows;

internal static class RecordingIpc
{
    public const string WorkerCommand = "--runmc-record-worker";
    public const string OkResponse = "OK";

    public static string GetPipeName(string targetName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(targetName));
        return "runmc-record-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    public static async Task<bool> IsRecordingAsync(string pipeName, CancellationToken cancellationToken)
    {
        try
        {
            string response = await SendCommandAsync(pipeName, "status", TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            return string.Equals(response, OkResponse, StringComparison.Ordinal);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public static async Task<string> SendStopAsync(string pipeName, CancellationToken cancellationToken)
    {
        try
        {
            return await SendCommandAsync(pipeName, "stop", TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new RunMcException("No recording is running for this target.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RunMcException("No recording is running for this target.", exception);
        }
    }

    private static async Task<string> SendCommandAsync(string pipeName, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
        await using StreamWriter writer = new(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        using StreamReader reader = new(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(command).ConfigureAwait(false);
        return await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false) ?? string.Empty;
    }
}