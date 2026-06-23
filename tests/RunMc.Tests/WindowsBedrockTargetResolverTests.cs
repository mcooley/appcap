namespace RunMc.Tests;

public sealed class WindowsBedrockTargetResolverTests
{
    [Fact]
    public async Task DefaultUsesRunningBedrockWhenAvailable()
    {
        TestWindowFinder windowFinder = new(new MinecraftWindow(TargetKind.RunningBedrock, 10));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.Default, CancellationToken.None);

        Assert.Equal(10, window.Handle);
        Assert.False(appLauncher.Launched);
    }

    [Fact]
    public async Task InstalledLaunchesBedrockWhenNoWindowIsRunning()
    {
        TestWindowFinder windowFinder = new(null, new MinecraftWindow(TargetKind.RunningBedrock, 20));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.InstalledBedrock, CancellationToken.None);

        Assert.Equal(20, window.Handle);
        Assert.True(appLauncher.Launched);
        Assert.Equal(BedrockPackage.Aumid, appLauncher.Aumid);
    }

    private sealed class TestWindowFinder : IWindowsMinecraftWindowFinder
    {
        private readonly Queue<MinecraftWindow?> windows;

        public TestWindowFinder(params MinecraftWindow?[] windows)
        {
            this.windows = new Queue<MinecraftWindow?>(windows);
        }

        public MinecraftWindow? TryFindWindow(string packageFamilyName, TargetKind target)
        {
            Assert.Equal(BedrockPackage.FamilyName, packageFamilyName);
            return windows.Count > 1 ? windows.Dequeue() : windows.Peek();
        }
    }

    private sealed class TestAppLauncher : IWindowsAppLauncher
    {
        public bool Launched { get; private set; }

        public string? Aumid { get; private set; }

        public void LaunchAumid(string aumid)
        {
            Launched = true;
            Aumid = aumid;
        }
    }
}