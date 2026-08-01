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
    private readonly SemaphoreSlim captureGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly InputDeviceAttachmentRegistry attachments;
    private AttachedCaptureSession? captureSession;
    private Task captureMonitor = Task.CompletedTask;
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
        attachments = new InputDeviceAttachmentRegistry(application.Name, WindowsTargetHost.SupportedInputDeviceTypes);
        foreach (InputDeviceType deviceType in WindowsTargetHost.SupportedInputDeviceTypes)
        {
            attachments.Attach(deviceType);
        }
    }

    public TargetApplication Application => application;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        TargetWindow? window = await TryResolveRunningAsync(cancellationToken).ConfigureAwait(false);
        if (window is not null)
        {
            _ = await EnsureCaptureSessionAsync(window, cancellationToken).ConfigureAwait(false);
        }

        captureMonitor = MonitorCaptureAsync(lifetimeCancellation.Token);
    }

    public Task AttachInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.AttachInputDeviceAsync(deviceType, token), cancellationToken);

    public Task RemoveInputDeviceAsync(InputDeviceType deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.RemoveInputDeviceAsync(deviceType, token), cancellationToken);

    public Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.ListInputDevicesAsync(token), cancellationToken);

    public Task TapAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.TapAsync(x, y, deviceType, token), cancellationToken);

    public Task MoveMouseAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.MoveMouseAsync(x, y, deviceType, token), cancellationToken);

    public Task ClickMouseAsync(int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.ClickMouseAsync(x, y, deviceType, token), cancellationToken);

    public Task TypeAsync(string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken) =>
        ExecuteAsync((client, token) => client.TypeAsync(textAndKeys, deviceType, token), cancellationToken);

    public async Task<CapturedFrame> CaptureFrameAsync(bool includeCursor, CancellationToken cancellationToken)
    {
        TargetWindow window = await targetResolver.ResolveRunningAsync(application, cancellationToken).ConfigureAwait(false);
        AttachedCaptureSession capture = await GetCaptureSessionAsync(window, cancellationToken).ConfigureAwait(false);
        return await capture.CaptureFrameAsync(includeCursor, cancellationToken).ConfigureAwait(false);
    }

    public Task<AttachedCaptureSession> GetCaptureSessionAsync(TargetWindow window, CancellationToken cancellationToken) =>
        EnsureCaptureSessionAsync(window, cancellationToken);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        try
        {
            captureMonitor.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }

        captureSession?.Dispose();
        lifetimeCancellation.Dispose();
        captureGate.Dispose();
        gate.Dispose();
    }

    private async Task MonitorCaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    TargetWindow? window = await TryResolveRunningAsync(cancellationToken).ConfigureAwait(false);
                    if (window is not null)
                    {
                        _ = await EnsureCaptureSessionAsync(window, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<TargetWindow?> TryResolveRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await targetResolver.ResolveRunningAsync(application, cancellationToken).ConfigureAwait(false);
        }
        catch (AppCapException)
        {
            return null;
        }
    }

    private async Task<AttachedCaptureSession> EnsureCaptureSessionAsync(TargetWindow window, CancellationToken cancellationToken)
    {
        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (captureSession is not null &&
                captureSession.WindowHandle == window.Handle &&
                !captureSession.Completion.IsCompleted)
            {
                return captureSession;
            }

            captureSession?.Dispose();
            AttachedCaptureSession replacement = new(window, lifetimeCancellation.Token);
            try
            {
                await replacement.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                replacement.Dispose();
                throw;
            }

            captureSession = replacement;
            return replacement;
        }
        finally
        {
            captureGate.Release();
        }
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

    private void ValidateStatus(TargetStatusResult status)
    {
        if (!string.Equals(status.ProtocolVersion, TargetProtocol.Version, StringComparison.Ordinal))
        {
            throw new AppCapException(
                $"Target '{application.Name}' speaks target protocol {status.ProtocolVersion}, but the worker requires {TargetProtocol.Version}.");
        }

        string[] expected = attachments.SupportedDevices.Select(static device => device.ToString()).ToArray();
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
