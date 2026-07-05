using AppCap.Windows;

namespace AppCap.Tests;

public sealed class TargetResolverTests
{
    private static readonly AppCapTargetConfig App = new() { Name = "app", Id = "Package_family!App" };
    private static readonly AppCapTargetConfig OtherApp = new() { Name = "other", Id = "Other_family!App" };
    private static readonly TargetConfiguration Target = new("target", [App]);

    [Fact]
    public async Task UsesRunningWindowWhenAvailable()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(App, new TargetWindow(Target, App, 10));
        TestTargetLauncher targetLauncher = new();
        TargetResolver resolver = new(windowFinder, targetLauncher);

        TargetWindow window = await resolver.ResolveAsync(Target, CancellationToken.None);

        Assert.Equal(10, window.Handle);
        Assert.Null(targetLauncher.Launched);
    }

    [Fact]
    public async Task LaunchesConfiguredAppWhenNoWindowIsRunning()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(App, null, new TargetWindow(Target, App, 20));
        TestTargetLauncher targetLauncher = new();
        TargetResolver resolver = new(windowFinder, targetLauncher);

        TargetWindow window = await resolver.ResolveAsync(Target, CancellationToken.None);

        Assert.Equal(20, window.Handle);
        Assert.Equal(App, targetLauncher.Launched);
    }

    [Fact]
    public async Task DefaultTargetChecksApplicationsInOrder()
    {
        TargetConfiguration target = new("default", [App, OtherApp]);
        TestWindowFinder windowFinder = new();
        windowFinder.Set(OtherApp, new TargetWindow(target, OtherApp, 30));
        TestTargetLauncher targetLauncher = new();
        TargetResolver resolver = new(windowFinder, targetLauncher);

        TargetWindow window = await resolver.ResolveAsync(target, CancellationToken.None);

        Assert.Equal(30, window.Handle);
        Assert.Equal([App, OtherApp], windowFinder.RequestedApplications);
        Assert.Null(targetLauncher.Launched);
    }

    private sealed class TestWindowFinder : IWindowFinder
    {
        private readonly Dictionary<AppCapTargetConfig, Queue<TargetWindow?>> windowsByApplication = [];

        public List<AppCapTargetConfig> RequestedApplications { get; } = [];

        public void Set(AppCapTargetConfig application, params TargetWindow?[] windows)
        {
            windowsByApplication[application] = new Queue<TargetWindow?>(windows);
        }

        public TargetWindow? TryFindWindow(TargetConfiguration target, AppCapTargetConfig application)
        {
            RequestedApplications.Add(application);
            if (!windowsByApplication.TryGetValue(application, out Queue<TargetWindow?>? windows))
            {
                return null;
            }

            return windows.Count > 1 ? windows.Dequeue() : windows.Peek();
        }
    }

    private sealed class TestTargetLauncher : ITargetLauncher
    {
        public AppCapTargetConfig? Launched { get; private set; }

        public void Launch(AppCapTargetConfig target)
        {
            Launched = target;
        }
    }
}
