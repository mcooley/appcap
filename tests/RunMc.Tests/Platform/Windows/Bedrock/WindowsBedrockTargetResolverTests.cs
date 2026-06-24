namespace RunMc.Tests;

public sealed class WindowsBedrockTargetResolverTests
{
    [Fact]
    public async Task DefaultUsesRunningBedrockWhenAvailable()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(BedrockPackage.RetailFamilyName, new MinecraftWindow(TargetKind.RunningBedrock, 10));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.Default, CancellationToken.None);

        Assert.Equal(10, window.Handle);
        Assert.False(appLauncher.Launched);
    }

    [Fact]
    public async Task DefaultPrioritizesPreviewBeforeEducation()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(BedrockPackage.PreviewFamilyName, new MinecraftWindow(TargetKind.RunningBedrockPreview, 20));
        windowFinder.Set(BedrockPackage.EducationFamilyName, new MinecraftWindow(TargetKind.RunningEducation, 30));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.Default, CancellationToken.None);

        Assert.Equal(20, window.Handle);
        Assert.Equal(
            [BedrockPackage.RetailFamilyName, BedrockPackage.PreviewFamilyName],
            windowFinder.RequestedPackageFamilyNames);
    }

    [Fact]
    public async Task InstalledLaunchesBedrockWhenNoWindowIsRunning()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(BedrockPackage.RetailFamilyName, null, new MinecraftWindow(TargetKind.RunningBedrock, 20));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.InstalledBedrock, CancellationToken.None);

        Assert.Equal(20, window.Handle);
        Assert.True(appLauncher.Launched);
        Assert.Equal(BedrockPackage.RetailAumid, appLauncher.Aumid);
    }

    [Fact]
    public async Task PreviewInstalledLaunchesPreviewPackage()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(BedrockPackage.PreviewFamilyName, null, new MinecraftWindow(TargetKind.RunningBedrockPreview, 30));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.InstalledBedrockPreview, CancellationToken.None);

        Assert.Equal(30, window.Handle);
        Assert.True(appLauncher.Launched);
        Assert.Equal(BedrockPackage.PreviewAumid, appLauncher.Aumid);
    }

    [Fact]
    public async Task EducationInstalledLaunchesEducationPackage()
    {
        TestWindowFinder windowFinder = new();
        windowFinder.Set(BedrockPackage.EducationFamilyName, null, new MinecraftWindow(TargetKind.RunningEducation, 40));
        TestAppLauncher appLauncher = new();
        WindowsBedrockTargetResolver resolver = new(windowFinder, appLauncher);

        MinecraftWindow window = await resolver.ResolveAsync(TargetKind.InstalledEducation, CancellationToken.None);

        Assert.Equal(40, window.Handle);
        Assert.True(appLauncher.Launched);
        Assert.Equal(BedrockPackage.EducationAumid, appLauncher.Aumid);
    }

    private sealed class TestWindowFinder : IWindowsMinecraftWindowFinder
    {
        private readonly Dictionary<string, Queue<MinecraftWindow?>> windowsByPackageFamilyName = [];

        public List<string> RequestedPackageFamilyNames { get; } = [];

        public void Set(string packageFamilyName, params MinecraftWindow?[] windows)
        {
            windowsByPackageFamilyName[packageFamilyName] = new Queue<MinecraftWindow?>(windows);
        }

        public MinecraftWindow? TryFindWindow(string packageFamilyName, TargetKind target)
        {
            RequestedPackageFamilyNames.Add(packageFamilyName);
            if (!windowsByPackageFamilyName.TryGetValue(packageFamilyName, out Queue<MinecraftWindow?>? windows))
            {
                return null;
            }

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