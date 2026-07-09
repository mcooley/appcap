using AppCap.Protocol;
using AppCap.Protocol.Worker;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AppCap.Windows;

internal static class RecordingIpc
{
    public static RecordingCommandListener CreateCommandListener(string targetName, IWorkerService service) => new(GetPipeName(targetName), service);

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
            throw new AppCapException($"A recording is already starting for target '{targetName}'.");
        }

        try
        {
            if (await IsRecordingAsync(targetName, cancellationToken).ConfigureAwait(false))
            {
                throw new AppCapException($"A recording is already running for target '{targetName}'.");
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
            JsonRpcResponse? response = await SendRequestAsync(GetPipeName(targetName), WorkerMethods.RecordingStatus, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (response?.Result is not { } result)
            {
                return false;
            }

            RecordingStatusResult? status = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.RecordingStatusResult);
            return status?.Recording ?? false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // Sends a stop command to the worker recording the target, asking it to finish
    // and save the recording. Returns true if a recording was stopped, false if no
    // recording is running. Throws when the worker reports a failure while stopping.
    public static Task<bool> SendStopAsync(string targetName, CancellationToken cancellationToken) =>
        SendTerminationAsync(targetName, WorkerMethods.RecordingStop, cancellationToken);

    // Sends a cancel command to the worker recording the target, asking it to stop
    // and discard the partial recording without saving an output file. Returns true
    // if a recording was cancelled, false if no recording is running. Throws when the
    // worker reports a failure while cancelling.
    public static Task<bool> SendCancelAsync(string targetName, CancellationToken cancellationToken) =>
        SendTerminationAsync(targetName, WorkerMethods.RecordingCancel, cancellationToken);

    // Asks the worker recording the target to capture a screenshot from its live capture
    // session, render any caption, and save it to the requested path. Returns true if the
    // recording worker acknowledged the screenshot, or false if no recording answered (for
    // example, the recording ended between the status probe and this request), so the
    // caller can fall back to an in-process capture.
    public static async Task<bool> CaptureScreenshotAsync(string targetName, ScreenshotRequest screenshot, CancellationToken cancellationToken)
    {
        JsonRpcResponse? response;
        try
        {
            JsonRpcRequest request = JsonRpcCodec.CreateRequest(
                WorkerMethods.Screenshot,
                Interlocked.Increment(ref nextRequestId),
                screenshot,
                WorkerProtocolJsonContext.Default.ScreenshotRequest);
            response = await SendRequestAsync(GetPipeName(targetName), request, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
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

        if (response is null)
        {
            return false;
        }

        if (response.Error is { } error)
        {
            throw new AppCapException(error.Message);
        }

        if (response.Result is not { } result)
        {
            return false;
        }

        ScreenshotResult? acknowledgement = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.ScreenshotResult);
        return acknowledgement?.Acknowledged ?? false;
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

    // Creates the server end of the recording pipe, restricted so that only the
    // current user can connect. FirstPipeInstance ensures we fail rather than bind
    // to a pipe another local process has already squatted on the well-known name.
    internal static NamedPipeServerStream CreateServerStream(string pipeName) =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 0,
            outBufferSize: 0,
            CreateCurrentUserPipeSecurity());

    private static long nextRequestId;

    internal static string GetPipeName(string targetName) => "appcap-record-" + HashTargetName(targetName);

    private static string GetStartLockName(string targetName) => "appcap-record-start-" + HashTargetName(targetName);

    private static string HashTargetName(string targetName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(targetName));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private static PipeSecurity CreateCurrentUserPipeSecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new AppCapException("Could not determine the current user to secure the recording pipe.");

        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static async Task<bool> SendTerminationAsync(string targetName, string method, CancellationToken cancellationToken)
    {
        JsonRpcResponse? response;
        try
        {
            response = await SendRequestAsync(GetPipeName(targetName), method, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // No response line means the worker went away without answering: treat it the
        // same as no recording running rather than a failure.
        if (response is null)
        {
            return false;
        }

        if (response.Error is { } error)
        {
            throw new AppCapException(error.Message);
        }

        return response.Result is not null;
    }

    private static async Task<JsonRpcResponse?> SendRequestAsync(string pipeName, string method, TimeSpan timeout, CancellationToken cancellationToken)
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(method, Interlocked.Increment(ref nextRequestId));
        return await SendRequestAsync(pipeName, request, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonRpcResponse?> SendRequestAsync(string pipeName, JsonRpcRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);

        await JsonRpcCodec.WriteRequestAsync(pipe, request, timeoutSource.Token).ConfigureAwait(false);
        return await JsonRpcCodec.ReadResponseAsync(pipe, timeoutSource.Token).ConfigureAwait(false);
    }

    // Server side of the recording IPC protocol. Owns the named-pipe instance the
    // recording worker uses to answer status pings and receive the stop command.
    internal sealed class RecordingCommandListener
    {
        private readonly string pipeName;
        private readonly IWorkerService service;

        internal RecordingCommandListener(string pipeName, IWorkerService service)
        {
            this.pipeName = pipeName;
            this.service = service;
        }

        // Answers status and screenshot requests until a stop command arrives, then
        // returns the pending request so the caller can acknowledge or fail it once it
        // has finished.
        public async Task<RecordingStopRequest> WaitForStopAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                NamedPipeServerStream pipe = CreateServerStream(pipeName);
                bool transferred = false;
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    JsonRpcRequest? request;
                    try
                    {
                        request = await JsonRpcCodec.ReadRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
                    }
                    catch (JsonException)
                    {
                        await JsonRpcCodec.WriteResponseAsync(
                            pipe,
                            JsonRpcCodec.CreateError(null, JsonRpcErrorCodes.ParseError, "Invalid JSON-RPC request."),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // The client connected but closed without sending a request; wait for
                    // the next connection.
                    if (request is null)
                    {
                        continue;
                    }

                    switch (request.Method)
                    {
                        case WorkerMethods.RecordingStop:
                            transferred = true;
                            return new RecordingStopRequest(pipe, RecordingStopMode.Save, request.Id);

                        case WorkerMethods.RecordingCancel:
                            transferred = true;
                            return new RecordingStopRequest(pipe, RecordingStopMode.Discard, request.Id);

                        default:
                            // Status, screenshot, and unknown methods share the same
                            // dispatch the in-proc worker uses, so the recording worker
                            // and the in-proc worker answer them identically.
                            await WorkerServer.HandleAsync(pipe, request, service, cancellationToken).ConfigureAwait(false);
                            break;
                    }
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

    // Identifies how a recording should end: save the captured output, or discard it.
    internal enum RecordingStopMode
    {
        Save,
        Discard,
    }

    // A pending stop request from a client. Exactly one of AcknowledgeAsync or
    // FailAsync sends the JSON-RPC response; Dispose closes the underlying connection.
    internal sealed class RecordingStopRequest : IDisposable
    {
        private readonly NamedPipeServerStream pipe;
        private readonly JsonElement? requestId;
        private bool responded;

        internal RecordingStopRequest(NamedPipeServerStream pipe, RecordingStopMode mode, JsonElement? requestId)
        {
            this.pipe = pipe;
            this.requestId = requestId;
            Mode = mode;
        }

        public RecordingStopMode Mode { get; }

        public Task AcknowledgeAsync(CancellationToken cancellationToken) =>
            RespondAsync(
                JsonRpcCodec.CreateSuccess(requestId, new RecordingCommandResult { Acknowledged = true }, WorkerProtocolJsonContext.Default.RecordingCommandResult),
                cancellationToken);

        public Task FailAsync(string message, CancellationToken cancellationToken) =>
            RespondAsync(JsonRpcCodec.CreateError(requestId, JsonRpcErrorCodes.RecordingFailed, message), cancellationToken);

        private async Task RespondAsync(JsonRpcResponse response, CancellationToken cancellationToken)
        {
            if (responded)
            {
                return;
            }

            responded = true;
            await JsonRpcCodec.WriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);
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
