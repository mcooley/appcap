using RunMc.Windows;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RunMc.Tests;

public sealed class RecordingIpcTests
{
    private static readonly TimeSpan ShortLockTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void ServerPipeIsRestrictedToCurrentUser()
    {
        string pipeName = "runmc-test-" + Guid.NewGuid().ToString("N");

        using NamedPipeServerStream pipe = RecordingIpc.CreateServerStream(pipeName);

        PipeSecurity security = pipe.GetAccessControl();
        AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        PipeAccessRule rule = Assert.IsType<PipeAccessRule>(Assert.Single(rules));

        using WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
        Assert.Equal(currentUser.User, rule.IdentityReference);
        Assert.Equal(PipeAccessRights.FullControl, rule.PipeAccessRights);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }

    [Fact]
    public async Task StartLockIsExclusiveUntilReleased()
    {
        string target = Guid.NewGuid().ToString();

        RecordingStartLock? first = await RecordingIpc.TryAcquireStartLockAsync(target, ShortLockTimeout, CancellationToken.None);
        Assert.NotNull(first);

        RecordingStartLock? second = await RecordingIpc.TryAcquireStartLockAsync(target, ShortLockTimeout, CancellationToken.None);
        Assert.Null(second);

        first!.Dispose();

        RecordingStartLock? third = await RecordingIpc.TryAcquireStartLockAsync(target, ShortLockTimeout, CancellationToken.None);
        Assert.NotNull(third);
        third!.Dispose();
    }

    [Fact]
    public async Task StartLockIsNotSharedAcrossTargets()
    {
        using RecordingStartLock? first = await RecordingIpc.TryAcquireStartLockAsync(Guid.NewGuid().ToString(), ShortLockTimeout, CancellationToken.None);
        using RecordingStartLock? second = await RecordingIpc.TryAcquireStartLockAsync(Guid.NewGuid().ToString(), ShortLockTimeout, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task IsRecordingReturnsFalseWhenNoWorkerIsListening()
    {
        bool recording = await RecordingIpc.IsRecordingAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(recording);
    }

    [Fact]
    public async Task ClientReachesListenerForTheSameTarget()
    {
        string target = Guid.NewGuid().ToString();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target);
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        // A status check for the same target finds the running listener.
        Assert.True(await RecordingIpc.IsRecordingAsync(target, cts.Token));

        // A stop for the same target is delivered to that listener and acknowledged.
        Task<bool> stopClient = RecordingIpc.SendStopAsync(target, cts.Token);
        using RecordingIpc.RecordingStopRequest stopRequest = await waitForStop;
        Assert.Equal(RecordingIpc.RecordingStopMode.Save, stopRequest.Mode);
        await stopRequest.AcknowledgeAsync(cts.Token);

        Assert.True(await stopClient);
    }

    [Fact]
    public async Task CancelReachesListenerAsDiscardRequest()
    {
        string target = Guid.NewGuid().ToString();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target);
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        // A cancel for the same target is delivered to that listener as a discard request.
        Task<bool> cancelClient = RecordingIpc.SendCancelAsync(target, cts.Token);
        using RecordingIpc.RecordingStopRequest cancelRequest = await waitForStop;
        Assert.Equal(RecordingIpc.RecordingStopMode.Discard, cancelRequest.Mode);
        await cancelRequest.AcknowledgeAsync(cts.Token);

        Assert.True(await cancelClient);
    }

    [Fact]
    public async Task ListenerReportsCancelFailureToClient()
    {
        string target = Guid.NewGuid().ToString();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target);
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        Task<bool> cancelClient = RecordingIpc.SendCancelAsync(target, cts.Token);
        using RecordingIpc.RecordingStopRequest cancelRequest = await waitForStop;
        await cancelRequest.FailAsync("cancel failed", cts.Token);

        RunMcException exception = await Assert.ThrowsAsync<RunMcException>(async () => await cancelClient);
        Assert.Equal("cancel failed", exception.Message);
    }

    [Fact]
    public async Task ListenerForOneTargetIsNotVisibleToOtherTargets()
    {
        string listeningTarget = Guid.NewGuid().ToString();
        string otherTarget = Guid.NewGuid().ToString();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(listeningTarget);
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        Assert.True(await RecordingIpc.IsRecordingAsync(listeningTarget, cts.Token));
        Assert.False(await RecordingIpc.IsRecordingAsync(otherTarget, cts.Token));

        // Stop the listener so its waiting task completes.
        Task<bool> stopClient = RecordingIpc.SendStopAsync(listeningTarget, cts.Token);
        using RecordingIpc.RecordingStopRequest stopRequest = await waitForStop;
        await stopRequest.AcknowledgeAsync(cts.Token);
        Assert.True(await stopClient);
    }

    [Fact]
    public async Task ListenerReportsStopFailureToClient()
    {
        string target = Guid.NewGuid().ToString();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target);
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(cts.Token);

        Task<bool> stopClient = RecordingIpc.SendStopAsync(target, cts.Token);
        using RecordingIpc.RecordingStopRequest stopRequest = await waitForStop;
        await stopRequest.FailAsync("capture failed", cts.Token);

        RunMcException exception = await Assert.ThrowsAsync<RunMcException>(async () => await stopClient);
        Assert.Equal("capture failed", exception.Message);
    }

    [Fact]
    public async Task CancellingListenerReleasesItsPipe()
    {
        string target = Guid.NewGuid().ToString();
        using CancellationTokenSource probe = new(TimeSpan.FromSeconds(15));

        RecordingIpc.RecordingCommandListener listener = RecordingIpc.CreateCommandListener(target);
        using CancellationTokenSource firstWait = new();
        Task<RecordingIpc.RecordingStopRequest> waitForStop = listener.WaitForStopAsync(firstWait.Token);

        // The listener is up and answering status pings.
        Assert.True(await RecordingIpc.IsRecordingAsync(target, probe.Token));

        // Cancelling the wait tears the listener down instead of leaking the pipe.
        await firstWait.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitForStop);

        // The pipe instance was released: a brand-new listener can bind the same
        // well-known name (it uses FirstPipeInstance, which fails if one is leaked)
        // and once again answers status pings for the target.
        RecordingIpc.RecordingCommandListener replacement = RecordingIpc.CreateCommandListener(target);
        using CancellationTokenSource secondWait = new();
        Task<RecordingIpc.RecordingStopRequest> rebound = replacement.WaitForStopAsync(secondWait.Token);

        Assert.True(await RecordingIpc.IsRecordingAsync(target, probe.Token));

        await secondWait.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await rebound);
    }
}
