using AppCap.Windows;

namespace AppCap.Tests;

public sealed class TargetResolverTests
{
    private static readonly TargetApplication App = new() { Name = "app", Id = "Package_family!App" };

    [Fact]
    public async Task UsesRunningWindowWhenAvailable()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(App, new TargetWindow(App, 10));
        TestTargetLauncher targetLauncher = new();
        TargetResolver resolver = new(windowFinder, targetLauncher);

        TargetWindow window = await resolver.ResolveAsync(App, CancellationToken.None);

        Assert.Equal(10, window.Handle);
        Assert.Null(targetLauncher.Launched);
    }

    [Fact]
    public async Task LaunchesConfiguredAppWhenNoWindowIsRunning()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(App, null, new TargetWindow(App, 20));
        TestTargetLauncher targetLauncher = new();
        TargetResolver resolver = new(windowFinder, targetLauncher);

        TargetWindow window = await resolver.ResolveAsync(App, CancellationToken.None);

        Assert.Equal(20, window.Handle);
        Assert.Equal(App, targetLauncher.Launched);
    }

    private sealed class TestWindowFinder : IWindowFinder
    {
        private readonly Dictionary<TargetApplication, Queue<TargetWindow?>> windowsByApplication = [];

        public void Set(TargetApplication application, params TargetWindow?[] windows)
        {
            windowsByApplication[application] = new Queue<TargetWindow?>(windows);
        }

        public TargetWindow? TryFindWindow(TargetApplication application)
        {
            if (!windowsByApplication.TryGetValue(application, out Queue<TargetWindow?>? windows))
            {
                return null;
            }

            return windows.Count > 1 ? windows.Dequeue() : windows.Peek();
        }
    }

    private sealed class TestTargetLauncher : ITargetLauncher
    {
        public TargetApplication? Launched { get; private set; }

        public void Launch(TargetApplication target)
        {
            Launched = target;
        }
    }
}
