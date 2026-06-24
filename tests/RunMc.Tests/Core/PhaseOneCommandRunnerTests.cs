namespace RunMc.Tests;

public sealed class PhaseOneCommandRunnerTests
{
    [Fact]
    public async Task FocusResolvesTargetAndBringsWindowToForeground()
    {
        TestServices services = new();
        PhaseOneCommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new FocusCommand(TargetKind.Default), CancellationToken.None);

        Assert.Equal(TargetKind.Default, services.TargetResolver.RequestedTarget);
        Assert.Equal(services.Window, services.WindowController.ForegroundWindow);
    }

    [Fact]
    public async Task ClickRejectsCoordinatesOutsideWindow()
    {
        TestServices services = new()
        {
            Bounds = new WindowBounds(10, 20, 100, 50),
        };
        PhaseOneCommandRunner runner = services.CreateRunner();

        RunMcException exception = await Assert.ThrowsAsync<RunMcException>(() =>
            runner.RunAsync(new ClickCommand(TargetKind.RunningBedrock, 100, 10), CancellationToken.None));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Null(services.InputInjector.Click);
    }

    [Fact]
    public async Task ClickInsideWindowInjectsClick()
    {
        TestServices services = new()
        {
            Bounds = new WindowBounds(10, 20, 100, 50),
        };
        PhaseOneCommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ClickCommand(TargetKind.RunningBedrock, 99, 49), CancellationToken.None);

        Assert.Equal((109, 69), services.InputInjector.Click);
        Assert.Equal(["foreground", "bounds", "click"], services.Events);
    }

    [Fact]
    public async Task ResizeDelegatesRequestedOuterSize()
    {
        TestServices services = new();
        PhaseOneCommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ResizeCommand(TargetKind.InstalledBedrock, 800, 600), CancellationToken.None);

        Assert.Equal((800, 600), services.WindowController.Resize);
    }

    [Fact]
    public async Task ScreenshotDelegatesOutputPath()
    {
        TestServices services = new();
        PhaseOneCommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ScreenshotCommand(TargetKind.Default, "test.png"), CancellationToken.None);

        Assert.Equal("test.png", services.ScreenshotCapture.OutputPath);
    }

    [Fact]
    public async Task RejectsNonPhaseOneTargets()
    {
        TestServices services = new();
        PhaseOneCommandRunner runner = services.CreateRunner();

        RunMcException exception = await Assert.ThrowsAsync<RunMcException>(() =>
            runner.RunAsync(new FocusCommand(TargetKind.RunningJava), CancellationToken.None));

        Assert.Contains("runningjava", exception.Message, StringComparison.Ordinal);
        Assert.Null(services.TargetResolver.RequestedTarget);
    }

    private sealed class TestServices
    {
        public MinecraftWindow Window { get; } = new(TargetKind.RunningBedrock, 123);

        public WindowBounds Bounds { get; init; } = new(0, 0, 640, 480);

        public List<string> Events { get; } = [];

        public TestTargetResolver TargetResolver { get; private set; } = null!;

        public TestWindowController WindowController { get; private set; } = null!;

        public TestInputInjector InputInjector { get; private set; } = null!;

        public TestScreenshotCapture ScreenshotCapture { get; private set; } = null!;

        public PhaseOneCommandRunner CreateRunner()
        {
            TargetResolver = new TestTargetResolver(Window);
            WindowController = new TestWindowController(Bounds, Events);
            InputInjector = new TestInputInjector(Events);
            ScreenshotCapture = new TestScreenshotCapture();

            return new PhaseOneCommandRunner(
                TargetResolver,
                WindowController,
                InputInjector,
                ScreenshotCapture);
        }
    }

    private sealed class TestTargetResolver : IMinecraftTargetResolver
    {
        private readonly MinecraftWindow window;

        public TestTargetResolver(MinecraftWindow window)
        {
            this.window = window;
        }

        public TargetKind? RequestedTarget { get; private set; }

        public Task<MinecraftWindow> ResolveAsync(TargetKind target, CancellationToken cancellationToken)
        {
            RequestedTarget = target;
            return Task.FromResult(window);
        }
    }

    private sealed class TestWindowController : IWindowController
    {
        private readonly WindowBounds bounds;
        private readonly List<string> events;

        public TestWindowController(WindowBounds bounds, List<string> events)
        {
            this.bounds = bounds;
            this.events = events;
        }

        public MinecraftWindow? ForegroundWindow { get; private set; }

        public (int Width, int Height)? Resize { get; private set; }

        public Task BringToForegroundAsync(MinecraftWindow window, CancellationToken cancellationToken)
        {
            ForegroundWindow = window;
            events.Add("foreground");
            return Task.CompletedTask;
        }

        public Task<WindowBounds> GetBoundsAsync(MinecraftWindow window, CancellationToken cancellationToken)
        {
            events.Add("bounds");
            return Task.FromResult(bounds);
        }

        public Task ResizeAsync(MinecraftWindow window, int width, int height, CancellationToken cancellationToken)
        {
            Resize = (width, height);
            return Task.CompletedTask;
        }
    }

    private sealed class TestInputInjector : IInputInjector
    {
        private readonly List<string> events;

        public TestInputInjector(List<string> events)
        {
            this.events = events;
        }

        public (int X, int Y)? Click { get; private set; }

        public Task ClickAsync(MinecraftWindow window, int screenX, int screenY, CancellationToken cancellationToken)
        {
            events.Add("click");
            Click = (screenX, screenY);
            return Task.CompletedTask;
        }
    }

    private sealed class TestScreenshotCapture : IScreenshotCapture
    {
        public string? OutputPath { get; private set; }

        public Task CapturePngAsync(MinecraftWindow window, string outputPath, CancellationToken cancellationToken)
        {
            OutputPath = outputPath;
            return Task.CompletedTask;
        }
    }
}