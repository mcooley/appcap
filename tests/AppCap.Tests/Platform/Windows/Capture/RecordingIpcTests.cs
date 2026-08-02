using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AppCap.Protocol.Worker;
using AppCap.Windows;

namespace AppCap.Tests;

// Exercises the client and server halves of the worker IPC over the real named-pipe
// transport, against a fake worker host. Uses a unique pipe name per test (via the
// test-only override) and runs serialized in the WorkerPipe collection so tests never
// contend for the same machine-wide pipe.
[Collection(WorkerPipeSerialization.Name)]
public sealed class RecordingIpcTests : IDisposable
{
    private static readonly TimeSpan ShortLockTimeout = TimeSpan.FromMilliseconds(200);

    public RecordingIpcTests() => RecordingIpc.PipeNameOverride = "appcap-test-" + Guid.NewGuid().ToString("N");

    public void Dispose() => RecordingIpc.PipeNameOverride = null;

    [Fact]
    public void ServerPipeIsRestrictedToCurrentUser()
    {
        string pipeName = "appcap-test-" + Guid.NewGuid().ToString("N");

        using NamedPipeServerStream pipe = RecordingIpc.CreateServerStream(pipeName, firstInstance: true);

        PipeSecurity security = pipe.GetAccessControl();
        AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        PipeAccessRule rule = Assert.IsType<PipeAccessRule>(Assert.Single(rules));

        using WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
        Assert.Equal(currentUser.User, rule.IdentityReference);
        Assert.Equal(PipeAccessRights.FullControl, rule.PipeAccessRights);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }

    [Fact]
    public async Task LaunchLockIsExclusiveUntilReleased()
    {
        WorkerLaunchLock? first = await RecordingIpc.TryAcquireLaunchLockAsync(ShortLockTimeout, CancellationToken.None);
        Assert.NotNull(first);

        WorkerLaunchLock? second = await RecordingIpc.TryAcquireLaunchLockAsync(ShortLockTimeout, CancellationToken.None);
        Assert.Null(second);

        first!.Dispose();

        WorkerLaunchLock? third = await RecordingIpc.TryAcquireLaunchLockAsync(ShortLockTimeout, CancellationToken.None);
        Assert.NotNull(third);
        third!.Dispose();
    }

    [Fact]
    public async Task IsRecordingReturnsFalseWhenNoWorkerIsListening()
    {
        bool recording = await RecordingIpc.IsRecordingAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(recording);
    }

    [Fact]
    public async Task PingReturnsFalseWhenNoWorkerIsRunning()
    {
        Assert.False(await RecordingIpc.PingAsync(CancellationToken.None));
        Assert.Null(await RecordingIpc.GetWorkerProcessIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TargetAttachmentRoundTripsThroughWorker()
    {
        TargetDescriptorRequest target = new() { TargetName = "notepad", ApplicationId = "Microsoft.Notepad_8wekyb3d8bbwe!App" };
        FakeWorkerHost host = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            await RecordingIpc.AttachTargetAsync(target, cts.Token);

            TargetDescriptorRequest attached = Assert.Single(await RecordingIpc.ListTargetsAsync(cts.Token));
            Assert.Equal(target.TargetName, attached.TargetName);
            Assert.Equal(target.ApplicationId, attached.ApplicationId);

            await RecordingIpc.DetachTargetAsync(target.TargetName, cts.Token);
            Assert.Empty(await RecordingIpc.ListTargetsAsync(cts.Token));
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task DuplicateTargetAttachmentIsAUsageError()
    {
        TargetDescriptorRequest target = new() { TargetName = "notepad", ApplicationId = "Microsoft.Notepad_8wekyb3d8bbwe!App" };
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            await RecordingIpc.AttachTargetAsync(target, cts.Token);
            AppCapException exception = await Assert.ThrowsAsync<AppCapException>(() => RecordingIpc.AttachTargetAsync(target, cts.Token));

            Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
            Assert.Contains("already attached", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task ClientReachesWorkerForTarget()
    {
        string target = Guid.NewGuid().ToString();
        FakeWorkerHost host = new(recording: [target]);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            Assert.True(await RecordingIpc.PingAsync(cts.Token));
            Assert.Equal(Environment.ProcessId, await RecordingIpc.GetWorkerProcessIdAsync(cts.Token));
            Assert.True(await RecordingIpc.IsRecordingAsync(target, cts.Token));

            Assert.True(await RecordingIpc.SendStopAsync(target, cts.Token));
            Assert.False(host.GetRecordingStatus(target).Recording);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task CancelIsDeliveredAsDiscard()
    {
        string target = Guid.NewGuid().ToString();
        FakeWorkerHost host = new(recording: [target]);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            Assert.True(await RecordingIpc.SendCancelAsync(target, cts.Token));
            Assert.True(host.LastStopDiscard);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task StopForUnknownTargetReturnsFalse()
    {
        string recordingTarget = Guid.NewGuid().ToString();
        string otherTarget = Guid.NewGuid().ToString();
        FakeWorkerHost host = new(recording: [recordingTarget]);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            Assert.True(await RecordingIpc.IsRecordingAsync(recordingTarget, cts.Token));
            Assert.False(await RecordingIpc.IsRecordingAsync(otherTarget, cts.Token));

            // A stop for a target the worker is not recording is "nothing to stop", not a failure.
            Assert.False(await RecordingIpc.SendStopAsync(otherTarget, cts.Token));
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task WorkerReportsStopFailureToClient()
    {
        string target = Guid.NewGuid().ToString();
        FakeWorkerHost host = new(recording: [target]) { StopFailWith = "capture failed" };
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            AppCapException exception = await Assert.ThrowsAsync<AppCapException>(async () => await RecordingIpc.SendStopAsync(target, cts.Token));
            Assert.Equal("capture failed", exception.Message);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task WorkerReportsStartFailureToClient()
    {
        string target = Guid.NewGuid().ToString();
        FakeWorkerHost host = new() { StartFailWith = "Target window could not be captured." };
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            RecordingStartRequest request = new() { TargetName = target, OutputPath = @"C:\out\rec.mp4", TimeLimitSeconds = 1800, IncludeCursor = false, IncludeAudio = false, Crop = new CropRectangle(10, 20, 300, 200) };
            AppCapException exception = await Assert.ThrowsAsync<AppCapException>(async () => await RecordingIpc.StartRecordingAsync(request, cts.Token));
            Assert.Equal("Target window could not be captured.", exception.Message);
            Assert.Equal(1800, host.LastStart!.TimeLimitSeconds);
            Assert.False(host.LastStart.IncludeCursor);
            Assert.False(host.LastStart.IncludeAudio);
            Assert.Equal(new CropRectangle(10, 20, 300, 200), host.LastStart.Crop);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task CaptionIsDeliveredToActiveRecording()
    {
        string target = Guid.NewGuid().ToString();
        FakeWorkerHost host = new(recording: [target]);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            Assert.True(await RecordingIpc.SendCaptionAsync(target, "Test caption", cts.Token));
            Assert.Equal(target, host.LastCaption!.TargetName);
            Assert.Equal("Test caption", host.LastCaption.Caption);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task InputDeviceCommandsReachWorker()
    {
        string target = Guid.NewGuid().ToString();
        TargetDescriptorRequest request = new() { TargetName = target, ApplicationId = "Package_family!App" };
        FakeWorkerHost host = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            await RecordingIpc.AttachInputDeviceAsync(request, InputDeviceType.Touch, cts.Token);
            await RecordingIpc.AttachInputDeviceAsync(request, InputDeviceType.Mouse, cts.Token);
            IReadOnlyList<InputDeviceStatus> devices = await RecordingIpc.ListInputDevicesAsync(request, cts.Token);
            await RecordingIpc.TapAsync(request, 150, 130, deviceType: null, cts.Token);
            await RecordingIpc.MoveMouseAsync(request, 160, 140, InputDeviceType.Mouse, cts.Token);
            await RecordingIpc.ClickMouseAsync(request, 170, 150, InputDeviceType.Mouse, cts.Token);

            Assert.Equal(target, host.LastInputDeviceAttach!.TargetName);
            Assert.Equal("mouse", host.LastInputDeviceAttach.DeviceType);
            Assert.Collection(
                devices,
                device =>
                {
                    Assert.Equal(InputDeviceType.Touch, device.DeviceType);
                    Assert.True(device.Attached);
                },
                device =>
                {
                    Assert.Equal(InputDeviceType.Keyboard, device.DeviceType);
                    Assert.False(device.Attached);
                },
                device =>
                {
                    Assert.Equal(InputDeviceType.Mouse, device.DeviceType);
                    Assert.True(device.Attached);
                });
            Assert.Equal(target, host.LastTap!.Value.Target.TargetName);
            Assert.Equal(150, host.LastTap.Value.X);
            Assert.Equal(130, host.LastTap.Value.Y);
            Assert.Equal((160, 140, (InputDeviceType?)InputDeviceType.Mouse), (host.LastMouseMove!.Value.X, host.LastMouseMove.Value.Y, host.LastMouseMove.Value.DeviceType));
            Assert.Equal((170, 150, (InputDeviceType?)InputDeviceType.Mouse), (host.LastMouseClick!.Value.X, host.LastMouseClick.Value.Y, host.LastMouseClick.Value.DeviceType));
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task InputSelectionErrorsMapToUsageExitCode()
    {
        string target = Guid.NewGuid().ToString();
        TargetDescriptorRequest request = new() { TargetName = target, ApplicationId = "Package_family!App" };
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            AppCapException exception = await Assert.ThrowsAsync<AppCapException>(async () =>
                await RecordingIpc.TapAsync(request, 10, 20, deviceType: null, cts.Token));

            Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
            Assert.Contains("No 'touch' input device is attached", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task ConcurrentRequestsAreNotBlockedByASlowStop()
    {
        string slowTarget = Guid.NewGuid().ToString();
        string otherTarget = Guid.NewGuid().ToString();
        FakeWorkerHost host = new(recording: [slowTarget, otherTarget]) { BlockStopForTarget = slowTarget };
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(host, cts.Token);
        try
        {
            // Begin a stop that blocks inside the worker until we release it.
            Task<bool> slowStop = RecordingIpc.SendStopAsync(slowTarget, cts.Token);

            // While that stop is in flight, other requests must still be served promptly,
            // proving the accept loop handles connections concurrently.
            Assert.True(await RecordingIpc.PingAsync(cts.Token));
            Assert.True(await RecordingIpc.IsRecordingAsync(otherTarget, cts.Token));

            Assert.False(slowStop.IsCompleted);
            host.StopBlock.SetResult();
            Assert.True(await slowStop);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task ServerReleasesPipeWhenCancelled()
    {
        string target = Guid.NewGuid().ToString();

        using (CancellationTokenSource firstCts = new(TimeSpan.FromSeconds(15)))
        {
            Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(recording: [target]), firstCts.Token);
            Assert.True(await RecordingIpc.IsRecordingAsync(target, firstCts.Token));
            await ShutdownAsync(firstCts, server);
        }

        // A brand-new server can bind the same well-known name (FirstPipeInstance would
        // fail if the previous one leaked its pipe) and once again answers.
        using CancellationTokenSource secondCts = new(TimeSpan.FromSeconds(15));
        Task<bool> replacement = RecordingIpc.RunServerAsync(new FakeWorkerHost(recording: [target]), secondCts.Token);
        try
        {
            Assert.True(await RecordingIpc.IsRecordingAsync(target, secondCts.Token));
        }
        finally
        {
            await ShutdownAsync(secondCts, replacement);
        }
    }

    private static async Task ShutdownAsync(CancellationTokenSource cts, Task<bool> server)
    {
        await cts.CancelAsync();
        try
        {
            await server;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
