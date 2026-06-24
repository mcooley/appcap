namespace RunMc.Tests;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task FocusResolvesTargetAndBringsWindowToForeground()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

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
        CommandRunner runner = services.CreateRunner();

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
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ClickCommand(TargetKind.RunningBedrock, 99, 49), CancellationToken.None);

        Assert.Equal((109, 69), services.InputInjector.Click);
        Assert.Equal(["foreground", "bounds", "click"], services.Events);
    }

    [Fact]
    public async Task HoverInsideWindowMovesCursor()
    {
        TestServices services = new()
        {
            Bounds = new WindowBounds(10, 20, 100, 50),
        };
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new HoverCommand(TargetKind.RunningBedrock, 99, 49), CancellationToken.None);

        Assert.Equal((109, 69), services.CursorMover.Move);
        Assert.Equal(["foreground", "bounds", "hover"], services.Events);
    }

    [Fact]
    public async Task HoverRejectsCoordinatesOutsideWindow()
    {
        TestServices services = new()
        {
            Bounds = new WindowBounds(10, 20, 100, 50),
        };
        CommandRunner runner = services.CreateRunner();

        RunMcException exception = await Assert.ThrowsAsync<RunMcException>(() =>
            runner.RunAsync(new HoverCommand(TargetKind.RunningBedrock, 100, 10), CancellationToken.None));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Null(services.CursorMover.Move);
    }

    [Fact]
    public async Task TypeBringsWindowToForegroundAndInjectsKeyboardInput()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();
        KeyboardAction[] actions = [new TextKeyboardAction("hello"), new KeyPressKeyboardAction([], KeyboardKey.Enter)];

        await runner.RunAsync(new TypeCommand(TargetKind.RunningBedrock, actions), CancellationToken.None);

        Assert.Same(actions, services.KeyboardInputInjector.Actions);
        Assert.Equal(["foreground", "type"], services.Events);
    }

    [Fact]
    public async Task ResizeDelegatesRequestedOuterSize()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ResizeCommand(TargetKind.InstalledBedrock, 800, 600), CancellationToken.None);

        Assert.Equal((800, 600), services.WindowController.Resize);
    }

    [Fact]
    public async Task ScreenshotDelegatesOutputPath()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ScreenshotCommand(TargetKind.Default, "test.png", IncludeCursor: true), CancellationToken.None);

        Assert.Equal("test.png", services.ScreenshotCapture.OutputPath);
        Assert.True(services.ScreenshotCapture.IncludeCursor);
    }

    [Fact]
    public async Task RejectsUnsupportedTargets()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

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

        public TestCursorMover CursorMover { get; private set; } = null!;

        public TestKeyboardInputInjector KeyboardInputInjector { get; private set; } = null!;

        public TestScreenshotCapture ScreenshotCapture { get; private set; } = null!;

        public CommandRunner CreateRunner()
        {
            TargetResolver = new TestTargetResolver(Window);
            WindowController = new TestWindowController(Bounds, Events);
            InputInjector = new TestInputInjector(Events);
            CursorMover = new TestCursorMover(Events);
            KeyboardInputInjector = new TestKeyboardInputInjector(Events);
            ScreenshotCapture = new TestScreenshotCapture();

            return new CommandRunner(
                TargetResolver,
                WindowController,
                InputInjector,
                CursorMover,
                KeyboardInputInjector,
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

    private sealed class TestKeyboardInputInjector : IKeyboardInputInjector
    {
        private readonly List<string> events;

        public TestKeyboardInputInjector(List<string> events)
        {
            this.events = events;
        }

        public IReadOnlyList<KeyboardAction>? Actions { get; private set; }

        public Task TypeAsync(MinecraftWindow window, IReadOnlyList<KeyboardAction> actions, CancellationToken cancellationToken)
        {
            events.Add("type");
            Actions = actions;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCursorMover : ICursorMover
    {
        private readonly List<string> events;

        public TestCursorMover(List<string> events)
        {
            this.events = events;
        }

        public (int X, int Y)? Move { get; private set; }

        public Task MoveToAsync(MinecraftWindow window, int screenX, int screenY, CancellationToken cancellationToken)
        {
            events.Add("hover");
            Move = (screenX, screenY);
            return Task.CompletedTask;
        }
    }

    private sealed class TestScreenshotCapture : IScreenshotCapture
    {
        public string? OutputPath { get; private set; }

        public bool? IncludeCursor { get; private set; }

        public Task CapturePngAsync(MinecraftWindow window, string outputPath, bool includeCursor, CancellationToken cancellationToken)
        {
            OutputPath = outputPath;
            IncludeCursor = includeCursor;
            return Task.CompletedTask;
        }
    }
}