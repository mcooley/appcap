using RunMc.Windows;

namespace RunMc.Tests;

public sealed class RecordingIpcTests
{
    private static readonly TimeSpan ShortLockTimeout = TimeSpan.FromMilliseconds(200);

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
        await stopRequest.AcknowledgeAsync(cts.Token);

        Assert.True(await stopClient);
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
}
