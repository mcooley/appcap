using AppCap.Protocol.Worker;
using AppCap.Windows;

namespace AppCap.Tests;

[Collection(WorkerPipeSerialization.Name)]
public sealed class TargetSessionControllerTests : IDisposable
{
    private static readonly TargetApplication First = new() { Name = "first", Id = "First_family!App" };
    private static readonly TargetApplication Second = new() { Name = "second", Id = "Second_family!App" };

    public TargetSessionControllerTests() => RecordingIpc.PipeNameOverride = "appcap-test-" + Guid.NewGuid().ToString("N");

    public void Dispose() => RecordingIpc.PipeNameOverride = null;

    [Fact]
    public async Task AttachWithoutNamePrefersFirstRunningTarget()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.SetRunning(Second);
        TargetSessionController controller = new(new TargetCatalog([First, Second]), windowFinder, new TestTargetResolver());
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            TargetApplication selected = await controller.AttachAsync(target: null, launch: false, cts.Token);
            IReadOnlyList<TargetSessionStatus> statuses = await controller.ListAsync(cts.Token);

            Assert.Equal(Second, selected);
            Assert.Collection(
                statuses,
                status =>
                {
                    Assert.Equal(First, status.Target);
                    Assert.False(status.Attached);
                    Assert.False(status.Running);
                },
                status =>
                {
                    Assert.Equal(Second, status.Target);
                    Assert.True(status.Attached);
                    Assert.True(status.Running);
                });
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task ResolveRequiresExactlyOneAttachmentWhenTargetIsOmitted()
    {
        TargetCatalog catalog = new([First, Second]);
        TargetSessionController controller = new(catalog, new TestWindowFinder(), new TestTargetResolver());
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            AppCapException none = await Assert.ThrowsAsync<AppCapException>(() => controller.ResolveAsync(null, cts.Token));
            Assert.Contains("No targets", none.Message, StringComparison.Ordinal);

            await RecordingIpc.AttachTargetAsync(CreateRequest(First), cts.Token);
            Assert.Equal(First, await controller.ResolveAsync(null, cts.Token));

            await RecordingIpc.AttachTargetAsync(CreateRequest(Second), cts.Token);
            AppCapException multiple = await Assert.ThrowsAsync<AppCapException>(() => controller.ResolveAsync(null, cts.Token));
            Assert.Contains("Multiple targets", multiple.Message, StringComparison.Ordinal);
            Assert.Equal(Second, await controller.ResolveAsync(Second, cts.Token));
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    [Fact]
    public async Task ExplicitDetachRejectsTargetThatIsNotAttached()
    {
        TargetSessionController controller = new(new TargetCatalog([First, Second]), new TestWindowFinder(), new TestTargetResolver());
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        Task<bool> server = RecordingIpc.RunServerAsync(new FakeWorkerHost(), cts.Token);
        try
        {
            await RecordingIpc.AttachTargetAsync(CreateRequest(First), cts.Token);

            AppCapException exception = await Assert.ThrowsAsync<AppCapException>(() => controller.DetachAsync(Second, cts.Token));

            Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
            Assert.Contains("not attached", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await ShutdownAsync(cts, server);
        }
    }

    private static TargetDescriptorRequest CreateRequest(TargetApplication target) =>
        new() { TargetName = target.Name, ApplicationId = target.Id };

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

    private sealed class TestWindowFinder : IWindowFinder
    {
        private readonly HashSet<TargetApplication> running = [];

        public void SetRunning(TargetApplication target) => running.Add(target);

        public TargetWindow? TryFindWindow(TargetApplication application) =>
            running.Contains(application) ? new TargetWindow(application, 123) : null;
    }

    private sealed class TestTargetResolver : ITargetResolver
    {
        public Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken) =>
            Task.FromResult(new TargetWindow(target, 123));

        public Task<TargetWindow> ResolveRunningAsync(TargetApplication target, CancellationToken cancellationToken) =>
            Task.FromResult(new TargetWindow(target, 123));
    }
}
