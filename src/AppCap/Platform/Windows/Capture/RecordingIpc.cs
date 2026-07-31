using AppCap.Protocol;
using AppCap.Protocol.Worker;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AppCap.Windows;

// Client and server plumbing for the internal worker protocol over its named-pipe
// transport. A single machine-wide worker (one per interactive user) owns the pipe and
// multiplexes every target/recording; clients connect to it to start/stop recordings,
// probe status, and request screenshots. All operations are keyed by target name so one
// worker can serve many recordings at once.
internal static class RecordingIpc
{
    private static long nextRequestId;

    // Probes whether a machine worker is currently running and reachable.
    public static async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            JsonRpcResponse? response = await SendRequestAsync(WorkerMethods.Ping, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            return response?.Result is not null;
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

    public static Task AttachTargetAsync(TargetDescriptorRequest target, CancellationToken cancellationToken) =>
        SendAcknowledgedAsync(
            WorkerMethods.TargetAttach,
            target,
            WorkerProtocolJsonContext.Default.TargetDescriptorRequest,
            cancellationToken);

    public static Task DetachTargetAsync(string targetName, CancellationToken cancellationToken) =>
        SendAcknowledgedAsync(
            WorkerMethods.TargetDetach,
            new TargetRequest { TargetName = targetName },
            WorkerProtocolJsonContext.Default.TargetRequest,
            cancellationToken);

    public static async Task<IReadOnlyList<TargetDescriptorRequest>> ListTargetsAsync(CancellationToken cancellationToken)
    {
        JsonRpcResponse? response;
        try
        {
            response = await SendRequestAsync(WorkerMethods.TargetList, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or JsonException)
        {
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }

        if (response?.Error is { } error)
        {
            throw CreateWorkerProtocolException(error);
        }

        if (response?.Result is not { } result)
        {
            return [];
        }

        AttachedTargetListResult? list = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.AttachedTargetListResult);
        return list?.Targets
            .Select(static target => new TargetDescriptorRequest { TargetName = target.TargetName, ApplicationId = target.ApplicationId })
            .ToArray() ?? [];
    }

    // Asks the worker to start a recording for a target and returns once it confirms the
    // recording is running. Throws AppCapException with the worker's reason if the target
    // is already recording, the capture cannot start, or the worker does not answer.
    public static async Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken)
    {
        JsonRpcRequest rpc = JsonRpcCodec.CreateRequest(
            WorkerMethods.RecordingStart,
            Interlocked.Increment(ref nextRequestId),
            request,
            WorkerProtocolJsonContext.Default.RecordingStartRequest);

        JsonRpcResponse? response;
        try
        {
            response = await SendRequestAsync(rpc, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AppCapException("The recording worker did not respond to the start request.");
        }
        catch (IOException exception)
        {
            throw new AppCapException("The recording worker did not respond to the start request.", exception);
        }

        if (response is null)
        {
            throw new AppCapException("The recording worker did not respond to the start request.");
        }

        if (response.Error is { } error)
        {
            throw new AppCapException(error.Message);
        }

        if (response.Result is null)
        {
            throw new AppCapException("The recording worker returned an empty start response.");
        }
    }

    public static async Task<bool> IsRecordingAsync(string targetName, CancellationToken cancellationToken)
    {
        try
        {
            RecordingStatusResult status = await GetRecordingStatusAsync(targetName, cancellationToken).ConfigureAwait(false);
            return status.Recording;
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

    public static async Task<RecordingStatusResult> GetRecordingStatusAsync(string targetName, CancellationToken cancellationToken)
    {
        JsonRpcResponse? response = await SendTargetRequestAsync(WorkerMethods.RecordingStatus, targetName, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        if (response?.Error is { } error)
        {
            throw CreateWorkerProtocolException(error);
        }

        if (response?.Result is not { } result)
        {
            throw new AppCapException("The worker did not return recording status.");
        }

        return JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.RecordingStatusResult) ??
            throw new AppCapException("The worker returned an empty recording status.");
    }

    // Sends a stop command to the worker, asking it to finish and save the target's
    // recording. Returns true if a recording was stopped, false if no recording is running
    // for the target (or no worker answered). Throws when the worker reports a failure.
    public static Task<bool> SendStopAsync(string targetName, CancellationToken cancellationToken) =>
        SendTerminationAsync(targetName, WorkerMethods.RecordingStop, cancellationToken);

    // Sends a cancel command to the worker, asking it to stop and discard the target's
    // partial recording. Returns true if a recording was cancelled, false if no recording
    // is running for the target (or no worker answered). Throws on a worker failure.
    public static Task<bool> SendCancelAsync(string targetName, CancellationToken cancellationToken) =>
        SendTerminationAsync(targetName, WorkerMethods.RecordingCancel, cancellationToken);

    public static Task<bool> SendCaptionAsync(string targetName, string caption, CancellationToken cancellationToken) =>
        SendCaptionAsync(new CaptionRequest { TargetName = targetName, Caption = caption }, cancellationToken);

    // Asks the worker to capture a screenshot from the target's live capture session,
    // render any caption, and save it to the requested path. Returns true if the worker
    // acknowledged the screenshot, or false if the target is no longer recording (for
    // example, the recording ended between the status probe and this request), so the
    // worker can report that the target is unavailable.
    public static async Task<bool> CaptureScreenshotAsync(string targetName, ScreenshotRequest screenshot, CancellationToken cancellationToken)
    {
        screenshot.TargetName = targetName;

        JsonRpcResponse? response;
        try
        {
            JsonRpcRequest request = JsonRpcCodec.CreateRequest(
                WorkerMethods.Screenshot,
                Interlocked.Increment(ref nextRequestId),
                screenshot,
                WorkerProtocolJsonContext.Default.ScreenshotRequest);
            response = await SendRequestAsync(request, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
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
            // The target stopped recording between the probe and the request: fall back to
            // report that the requested target is unavailable.
            if (error.Code == JsonRpcErrorCodes.NotRecording)
            {
                return false;
            }

            throw new AppCapException(error.Message);
        }

        if (response.Result is not { } result)
        {
            return false;
        }

        ScreenshotResult? acknowledgement = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.ScreenshotResult);
        return acknowledgement?.Acknowledged ?? false;
    }

    public static Task AttachInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedInputAsync(
            WorkerMethods.InputDeviceAttach,
            new InputDeviceRequest
            {
                TargetName = target.TargetName,
                ApplicationId = target.ApplicationId,
                DeviceType = deviceType.ToString(),
            },
            WorkerProtocolJsonContext.Default.InputDeviceRequest,
            cancellationToken);

    public static Task RemoveInputDeviceAsync(TargetDescriptorRequest target, InputDeviceType deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedInputAsync(
            WorkerMethods.InputDeviceRemove,
            new InputDeviceRequest
            {
                TargetName = target.TargetName,
                ApplicationId = target.ApplicationId,
                DeviceType = deviceType.ToString(),
            },
            WorkerProtocolJsonContext.Default.InputDeviceRequest,
            cancellationToken);

    public static async Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetDescriptorRequest target, CancellationToken cancellationToken)
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(
            WorkerMethods.InputDeviceList,
            Interlocked.Increment(ref nextRequestId),
            target,
            WorkerProtocolJsonContext.Default.TargetDescriptorRequest);

        JsonRpcResponse? response = await SendRequestAsync(request, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw new AppCapException("The worker did not respond to the input-device list request.");
        }

        if (response.Error is { } error)
        {
            throw CreateWorkerProtocolException(error);
        }

        if (response.Result is not { } result)
        {
            throw new AppCapException("The worker returned an empty input-device list response.");
        }

        WorkerInputDeviceListResult? list = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.WorkerInputDeviceListResult);
        if (list is null)
        {
            throw new AppCapException("The worker returned an empty input-device list response.");
        }

        return list.Devices
            .Select(static device => new InputDeviceStatus(InputDeviceType.Parse(device.DeviceType), device.Attached))
            .ToArray();
    }

    public static Task TapAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedInputAsync(
            WorkerMethods.InputTap,
            new PointerInputRequest
            {
                TargetName = target.TargetName,
                ApplicationId = target.ApplicationId,
                X = x,
                Y = y,
                DeviceType = deviceType?.ToString(),
            },
            WorkerProtocolJsonContext.Default.PointerInputRequest,
            cancellationToken);

    public static Task MoveMouseAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendPointerInputAsync(WorkerMethods.InputMouseMove, target, x, y, deviceType, cancellationToken);

    public static Task ClickMouseAsync(TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendPointerInputAsync(WorkerMethods.InputMouseClick, target, x, y, deviceType, cancellationToken);

    private static Task SendPointerInputAsync(string method, TargetDescriptorRequest target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedInputAsync(
            method,
            new PointerInputRequest
            {
                TargetName = target.TargetName,
                ApplicationId = target.ApplicationId,
                X = x,
                Y = y,
                DeviceType = deviceType?.ToString(),
            },
            WorkerProtocolJsonContext.Default.PointerInputRequest,
            cancellationToken);

    public static Task TypeAsync(TargetDescriptorRequest target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        SendAcknowledgedInputAsync(
            WorkerMethods.InputType,
            new KeyboardInputRequest
            {
                TargetName = target.TargetName,
                ApplicationId = target.ApplicationId,
                TextAndKeys = textAndKeys,
                DeviceType = deviceType?.ToString(),
            },
            WorkerProtocolJsonContext.Default.KeyboardInputRequest,
            cancellationToken);

    // Acquires the cross-process lock that serializes just-in-time worker launches, so two
    // clients that both find no worker running cannot spawn competing workers. Returns null
    // if the lock could not be acquired within the timeout. Dispose to release.
    public static async Task<WorkerLaunchLock?> TryAcquireLaunchLockAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Semaphore? semaphore = new(1, 1, GetLaunchLockName());
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

            WorkerLaunchLock launchLock = new(semaphore);
            semaphore = null;
            return launchLock;
        }
        finally
        {
            semaphore?.Dispose();
        }
    }

    // Runs the machine-wide worker's named-pipe server: accepts connections on the
    // well-known per-user pipe and dispatches each to the worker host over the worker
    // protocol. Connections are handled concurrently — a slow recording.stop for one target
    // never blocks a status or start for another. Returns false if another worker already
    // owns the pipe (this process should then exit); returns true when the loop is
    // cancelled during a normal shutdown.
    public static async Task<bool> RunServerAsync(IWorkerHost host, CancellationToken cancellationToken)
    {
        string pipeName = GetPipeName();
        bool first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateServerStream(pipeName, first);
            }
            catch (IOException) when (first)
            {
                // FirstPipeInstance failed: another worker already owns the pipe.
                return false;
            }

            first = false;

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch (IOException)
            {
                server.Dispose();
                continue;
            }

            // Handle this connection on its own task and loop immediately to create the next
            // listening instance, so concurrent clients are served without head-of-line
            // blocking.
            _ = HandleAndDisposeAsync(server, host, cancellationToken);
        }

        return true;
    }

    private static async Task HandleAndDisposeAsync(NamedPipeServerStream pipe, IWorkerHost host, CancellationToken cancellationToken)
    {
        try
        {
            await WorkerServer.HandleConnectionAsync(pipe, host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A single bad connection must never take down the accept loop.
        }
        finally
        {
            pipe.Dispose();
        }
    }

    // Creates a server end of the machine worker pipe, restricted so that only the current
    // user can connect. The first instance uses FirstPipeInstance so a second worker fails
    // to bind rather than squatting the well-known name; additional concurrent instances
    // omit it. MaxAllowedServerInstances lets many connections be served at once.
    internal static NamedPipeServerStream CreateServerStream(string pipeName, bool firstInstance) =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            firstInstance ? PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance : PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            CreateCurrentUserPipeSecurity());

    internal static string GetPipeName() => PipeNameOverride ?? ("appcap-worker-" + CurrentUserHash());

    // Test-only override so unit tests can bind a unique, hermetic pipe name instead of the
    // shared per-user machine pipe. Never set in production.
    internal static string? PipeNameOverride { get; set; }

    private static string GetLaunchLockName() => "appcap-worker-launch-" + CurrentUserHash();

    private static string CurrentUserHash()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new AppCapException("Could not determine the current user for the worker pipe.");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(user.Value));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private static PipeSecurity CreateCurrentUserPipeSecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new AppCapException("Could not determine the current user to secure the worker pipe.");

        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static async Task<bool> SendTerminationAsync(string targetName, string method, CancellationToken cancellationToken)
    {
        JsonRpcResponse? response;
        try
        {
            response = await SendTargetRequestAsync(method, targetName, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
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

        // No response line means the worker went away without answering: treat it the
        // same as no recording running rather than a failure.
        if (response is null)
        {
            return false;
        }

        if (response.Error is { } error)
        {
            // The worker is running but has no recording for this target: nothing to stop.
            if (error.Code == JsonRpcErrorCodes.NotRecording)
            {
                return false;
            }

            throw new AppCapException(error.Message);
        }

        return response.Result is not null;
    }

    private static async Task<bool> SendCaptionAsync(CaptionRequest caption, CancellationToken cancellationToken)
    {
        JsonRpcResponse? response;
        try
        {
            JsonRpcRequest request = JsonRpcCodec.CreateRequest(
                WorkerMethods.RecordingCaption,
                Interlocked.Increment(ref nextRequestId),
                caption,
                WorkerProtocolJsonContext.Default.CaptionRequest);
            response = await SendRequestAsync(request, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
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

        if (response?.Error is { } error)
        {
            if (error.Code == JsonRpcErrorCodes.NotRecording)
            {
                return false;
            }

            throw new AppCapException(error.Message);
        }

        return response?.Result is not null;
    }

    private static async Task<JsonRpcResponse?> SendTargetRequestAsync(string method, string targetName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(
            method,
            Interlocked.Increment(ref nextRequestId),
            new TargetRequest { TargetName = targetName },
            WorkerProtocolJsonContext.Default.TargetRequest);
        return await SendRequestAsync(request, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonRpcResponse?> SendRequestAsync(string method, TimeSpan timeout, CancellationToken cancellationToken)
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(method, Interlocked.Increment(ref nextRequestId));
        return await SendRequestAsync(request, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonRpcResponse?> SendRequestAsync(JsonRpcRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using NamedPipeClientStream pipe = new(".", GetPipeName(), PipeDirection.InOut, PipeOptions.Asynchronous);

        // Connecting has its own short timeout so a missing worker fails fast, even when the
        // overall request timeout is long to allow for a deferred stop/start response.
        using (CancellationTokenSource connectSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token))
        {
            connectSource.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await pipe.ConnectAsync(connectSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (connectSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // No worker is listening on the pipe within the timeout: report it as a
                // timeout so callers uniformly treat "no worker" the same as an unreachable
                // worker.
                throw new TimeoutException("Timed out connecting to the recording worker.");
            }
        }

        await JsonRpcCodec.WriteRequestAsync(pipe, request, timeoutSource.Token).ConfigureAwait(false);
        return await JsonRpcCodec.ReadResponseAsync(pipe, timeoutSource.Token).ConfigureAwait(false);
    }

    private static async Task SendAcknowledgedInputAsync<TParams>(
        string method,
        TParams parameters,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken cancellationToken)
    {
        await SendAcknowledgedAsync(method, parameters, paramsTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendAcknowledgedAsync<TParams>(
        string method,
        TParams parameters,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken cancellationToken)
    {
        JsonRpcRequest request = JsonRpcCodec.CreateRequest(
            method,
            Interlocked.Increment(ref nextRequestId),
            parameters,
            paramsTypeInfo);

        JsonRpcResponse? response = await SendRequestAsync(request, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw new AppCapException($"The worker did not respond to '{method}'.");
        }

        if (response.Error is { } error)
        {
            throw CreateWorkerProtocolException(error);
        }

        if (response.Result is not { } result)
        {
            throw new AppCapException($"The worker returned an empty response for '{method}'.");
        }

        RecordingCommandResult? acknowledgement = JsonRpcCodec.ReadResult(result, WorkerProtocolJsonContext.Default.RecordingCommandResult);
        if (acknowledgement is not { Acknowledged: true })
        {
            throw new AppCapException($"The worker did not acknowledge '{method}'.");
        }
    }

    private static AppCapException CreateWorkerProtocolException(JsonRpcError error) =>
        new(error.Message, MapWorkerProtocolExitCode(error.Code));

    private static int MapWorkerProtocolExitCode(int errorCode) => errorCode switch
    {
        JsonRpcErrorCodes.InvalidParams or
        JsonRpcErrorCodes.UnsupportedInputDevice or
        JsonRpcErrorCodes.InputDeviceAlreadyAttached or
        JsonRpcErrorCodes.InputDeviceNotAttached or
        JsonRpcErrorCodes.InvalidInputDeviceSelection or
        JsonRpcErrorCodes.TargetAlreadyAttached or
        JsonRpcErrorCodes.TargetNotAttached => ExitCodes.UsageError,
        _ => ExitCodes.OperationalError,
    };
}

// A cross-process lock, backed by a named semaphore, that serializes just-in-time worker
// launches so competing clients cannot each spawn a worker.
internal sealed class WorkerLaunchLock : IDisposable
{
    private readonly Semaphore semaphore;
    private bool released;

    internal WorkerLaunchLock(Semaphore semaphore) => this.semaphore = semaphore;

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
