using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace RunMc.Windows;

internal static class RecordingIpc
{
    public static RecordingCommandListener CreateCommandListener(string targetName) => new(GetPipeName(targetName));

    // Atomically claims the right to start a recording for a target: acquires a
    // cross-process lock that serializes starts, then verifies no recording is
    // already running. Throws if another start is already in progress or a
    // recording is already running. Hold the returned lock until the worker has
    // launched, then dispose it to release.
    public static async Task<RecordingStartLock> BeginStartAsync(string targetName, CancellationToken cancellationToken)
    {
        RecordingStartLock? startLock = await TryAcquireStartLockAsync(targetName, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (startLock is null)
        {
            throw new RunMcException($"A recording is already starting for target '{targetName}'.");
        }

        try
        {
            if (await IsRecordingAsync(targetName, cancellationToken).ConfigureAwait(false))
            {
                throw new RunMcException($"A recording is already running for target '{targetName}'.");
            }
        }
        catch
        {
            startLock.Dispose();
            throw;
        }

        return startLock;
    }

    public static async Task<bool> IsRecordingAsync(string targetName, CancellationToken cancellationToken)
    {
        try
        {
            string response = await SendCommandAsync(GetPipeName(targetName), StatusCommand, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
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

    // Sends a stop command to the worker recording the target. Returns true if a
    // recording was stopped, false if no recording is running. Throws when the
    // worker reports a failure while stopping.
    public static async Task<bool> SendStopAsync(string targetName, CancellationToken cancellationToken)
    {
        string response;
        try
        {
            response = await SendCommandAsync(GetPipeName(targetName), StopCommand, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (string.Equals(response, OkResponse, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        throw new RunMcException(response);
    }

    internal static async Task<RecordingStartLock?> TryAcquireStartLockAsync(string targetName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Semaphore? semaphore = new(1, 1, GetStartLockName(targetName));
        try
        {
            bool acquired = await Task.Run(
                () =>
                {
                    WaitHandle[] handles = [semaphore, cancellationToken.WaitHandle];
                    int index = WaitHandle.WaitAny(handles, timeout);
                    if (index == 1)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    return index == 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (!acquired)
            {
                return null;
            }

            RecordingStartLock startLock = new(semaphore);
            semaphore = null;
            return startLock;
        }
        finally
        {
            semaphore?.Dispose();
        }
    }

    private const string OkResponse = "ok";
    private const string StatusCommand = "status";
    private const string StopCommand = "stop";
    private const string UnknownCommandResponse = "unknown-command";

    private static string GetPipeName(string targetName) => "runmc-record-" + HashTargetName(targetName);

    private static string GetStartLockName(string targetName) => "runmc-record-start-" + HashTargetName(targetName);

    private static string HashTargetName(string targetName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(targetName));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private static async Task<string> SendCommandAsync(string pipeName, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
        await WriteLineAsync(pipe, command, timeoutSource.Token).ConfigureAwait(false);
        return await ReadLineAsync(pipe, timeoutSource.Token).ConfigureAwait(false) ?? string.Empty;
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteLineAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        await using StreamWriter writer = new(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    // Server side of the recording IPC protocol. Owns the named-pipe instance the
    // recording worker uses to answer status pings and receive the stop command.
    internal sealed class RecordingCommandListener
    {
        private readonly string pipeName;

        internal RecordingCommandListener(string pipeName) => this.pipeName = pipeName;

        // Answers status pings until a stop command arrives, then returns the pending
        // request so the caller can acknowledge or fail it once it has finished.
        public async Task<RecordingStopRequest> WaitForStopAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                NamedPipeServerStream pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                bool transferred = false;
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    string? command = await ReadLineAsync(pipe, cancellationToken).ConfigureAwait(false);

                    if (string.Equals(command, StopCommand, StringComparison.Ordinal))
                    {
                        transferred = true;
                        return new RecordingStopRequest(pipe);
                    }

                    string response = string.Equals(command, StatusCommand, StringComparison.Ordinal)
                        ? OkResponse
                        : UnknownCommandResponse;
                    await WriteLineAsync(pipe, response, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (!transferred)
                    {
                        pipe.Dispose();
                    }
                }
            }
        }
    }

    // A pending stop request from a client. Exactly one of AcknowledgeAsync or
    // FailAsync sends the response; Dispose closes the underlying connection.
    internal sealed class RecordingStopRequest : IDisposable
    {
        private readonly NamedPipeServerStream pipe;
        private bool responded;

        internal RecordingStopRequest(NamedPipeServerStream pipe) => this.pipe = pipe;

        public Task AcknowledgeAsync(CancellationToken cancellationToken) => RespondAsync(OkResponse, cancellationToken);

        public Task FailAsync(string message, CancellationToken cancellationToken) => RespondAsync(message, cancellationToken);

        private async Task RespondAsync(string response, CancellationToken cancellationToken)
        {
            if (responded)
            {
                return;
            }

            responded = true;
            await WriteLineAsync(pipe, response, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => pipe.Dispose();
    }
}

internal sealed class RecordingStartLock : IDisposable
{
    private readonly Semaphore semaphore;
    private bool released;

    internal RecordingStartLock(Semaphore semaphore) => this.semaphore = semaphore;

    public void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;
        semaphore.Release();
        semaphore.Dispose();
    }
}