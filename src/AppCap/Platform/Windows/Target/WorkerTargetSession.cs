using AppCap.Protocol;
using AppCap.Protocol.Target;

namespace AppCap.Windows;

internal sealed class WorkerTargetSession : IDisposable
{
    private readonly TargetApplication application;
    private readonly ITargetResolver targetResolver;
    private readonly IWindowController windowController;
    private readonly IInputInjector inputInjector;
    private readonly IKeyboardInputInjector keyboardInputInjector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private InputDeviceAttachmentRegistry? attachments;
    private bool disposed;

    public WorkerTargetSession(
        TargetApplication application,
        ITargetResolver targetResolver,
        IWindowController windowController,
        IInputInjector inputInjector,
        IKeyboardInputInjector keyboardInputInjector)
    {
        this.application = application;
        this.targetResolver = targetResolver;
        this.windowController = windowController;
        this.inputInjector = inputInjector;
        this.keyboardInputInjector = keyboardInputInjector;
    }

    public TargetApplication Application => application;

    public Task AttachInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.AttachInputDeviceAsync(deviceType, token), cancellationToken);

    public Task RemoveInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.RemoveInputDeviceAsync(deviceType, token), cancellationToken);

    public Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.ListInputDevicesAsync(token), cancellationToken);

    public Task TapAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (client, token) =>
            {
                await EnsureInputDeviceAttachedAsync(client, InputDeviceType.Touch, token).ConfigureAwait(false);
                await client.TapAsync(x, y, deviceType, token).ConfigureAwait(false);
            },
            cancellationToken);

    public Task TypeAsync(string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (client, token) =>
            {
                await EnsureInputDeviceAttachedAsync(client, InputDeviceType.Keyboard, token).ConfigureAwait(false);
                await client.TypeAsync(textAndKeys, deviceType, token).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.CaptureFrameAsync(includeCursor, token), cancellationToken);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
    }

    private async Task ExecuteAsync(Func<TargetClient, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteAsync<object?>(
            async (client, token) =>
            {
                await operation(client, token).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteAsync<TResult>(Func<TargetClient, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TargetWindow window = await targetResolver.ResolveRunningAsync(application, cancellationToken).ConfigureAwait(false);
            await using InProcTargetProtocolSession session = await CreateProtocolSessionAsync(window, cancellationToken).ConfigureAwait(false);
            return await operation(session.Client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<InProcTargetProtocolSession> CreateProtocolSessionAsync(TargetWindow window, CancellationToken cancellationToken)
    {
        attachments ??= new InputDeviceAttachmentRegistry(application.Name, WindowsTargetHost.SupportedInputDeviceTypes);
        WindowsTargetHost host = new(window, windowController, inputInjector, keyboardInputInjector, attachments);
        InProcTargetProtocolSession session = new(host, cancellationToken);
        try
        {
            TargetStatusResult status = await session.Client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            ValidateStatus(status);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureInputDeviceAttachedAsync(TargetClient client, InputDeviceType requiredDevice, CancellationToken cancellationToken)
    {
        IReadOnlyList<InputDeviceStatus> devices = await client.ListInputDevicesAsync(cancellationToken).ConfigureAwait(false);
        InputDeviceStatus? device = devices.FirstOrDefault(candidate => candidate.DeviceType == requiredDevice);
        if (device is null)
        {
            throw new AppCapException($"Input device '{requiredDevice}' is not supported by the target.");
        }

        if (!device.Attached)
        {
            await client.AttachInputDeviceAsync(requiredDevice, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ValidateStatus(TargetStatusResult status)
    {
        if (!string.Equals(status.ProtocolVersion, TargetProtocol.Version, StringComparison.Ordinal))
        {
            throw new AppCapException(
                $"Target '{application.Name}' speaks target protocol {status.ProtocolVersion}, but the worker requires {TargetProtocol.Version}.");
        }

        string[] expected = attachments?.SupportedDevices.Select(static device => device.ToString()).ToArray() ?? [];
        string[] actual = status.SupportedInputDevices ?? [];
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new AppCapException(
                $"Target '{application.Name}' reported input devices '{string.Join(", ", actual)}', but the worker expected '{string.Join(", ", expected)}'.");
        }
    }

    private sealed class InProcTargetProtocolSession : IAsyncDisposable
    {
        private readonly Stream clientStream;
        private readonly Stream serverStream;
        private readonly CancellationTokenSource hostCancellation;
        private readonly Task serveTask;

        public InProcTargetProtocolSession(ITargetHost host, CancellationToken cancellationToken)
        {
            (Stream client, Stream server) = InProcDuplexTransport.CreatePair();
            clientStream = client;
            serverStream = server;
            hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            serveTask = TargetServer.ServeAsync(server, host, hostCancellation.Token);
            Client = new TargetClient(client);
        }

        public TargetClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            clientStream.Dispose();
            serverStream.Dispose();
            await hostCancellation.CancelAsync().ConfigureAwait(false);
            hostCancellation.Dispose();
            await DrainAsync(serveTask).ConfigureAwait(false);
        }

        private static async Task DrainAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}
