namespace AppCap.Tests;

public sealed class CommandRunnerTests
{
    private static readonly AppCapTargetConfig Application = new() { Name = "target", Id = "Package_family!App" };
    private static readonly TargetConfiguration Target = new("target", [Application]);

    [Fact]
    public async Task FocusResolvesTargetAndBringsWindowToForeground()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new FocusCommand(Target), CancellationToken.None);

        Assert.Equal(Target, services.TargetResolver.RequestedTarget);
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

        AppCapException exception = await Assert.ThrowsAsync<AppCapException>(() =>
            runner.RunAsync(new ClickCommand(Target, 100, 10), CancellationToken.None));

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

        await runner.RunAsync(new ClickCommand(Target, 99, 49), CancellationToken.None);

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

        await runner.RunAsync(new HoverCommand(Target, 99, 49), CancellationToken.None);

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

        AppCapException exception = await Assert.ThrowsAsync<AppCapException>(() =>
            runner.RunAsync(new HoverCommand(Target, 100, 10), CancellationToken.None));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Null(services.CursorMover.Move);
    }

    [Fact]
    public async Task TypeBringsWindowToForegroundAndInjectsKeyboardInput()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();
        KeyboardAction[] actions = [new TextKeyboardAction("hello"), new KeyPressKeyboardAction([], KeyboardKey.Enter)];

        await runner.RunAsync(new TypeCommand(Target, actions), CancellationToken.None);

        Assert.Same(actions, services.KeyboardInputInjector.Actions);
        Assert.Equal(["foreground", "type"], services.Events);
    }

    [Fact]
    public async Task ResizeDelegatesRequestedOuterSize()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ResizeCommand(Target, 800, 600), CancellationToken.None);

        Assert.Equal((800, 600), services.WindowController.Resize);
    }

    [Fact]
    public async Task ScreenshotDelegatesOutputPath()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new ScreenshotCommand(Target, "test.png", IncludeCursor: true, Caption: "Test caption"), CancellationToken.None);

        Assert.Equal("test.png", services.ScreenshotCapture.OutputPath);
        Assert.True(services.ScreenshotCapture.IncludeCursor);
        Assert.Equal("Test caption", services.ScreenshotCapture.Caption);
    }

    [Fact]
    public async Task RecordStartResolvesTargetAndStartsRecording()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new RecordStartCommand(Target, "recording.mp4", TimeSpan.FromMinutes(45)), CancellationToken.None);

        Assert.Equal(Target, services.TargetResolver.RequestedTarget);
        Assert.Equal(services.Window, services.RecordingController.StartWindow);
        Assert.Equal("recording.mp4", services.RecordingController.OutputPath);
        Assert.Equal(TimeSpan.FromMinutes(45), services.RecordingController.TimeLimit);
    }

    [Fact]
    public async Task RecordStopStopsTargetRecordingWithoutResolvingWindow()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new RecordStopCommand(Target), CancellationToken.None);

        Assert.Null(services.TargetResolver.RequestedTarget);
        Assert.Equal(Target, services.RecordingController.StopTarget);
    }

    [Fact]
    public async Task RecordCancelCancelsTargetRecordingWithoutResolvingWindow()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new RecordCancelCommand(Target), CancellationToken.None);

        Assert.Null(services.TargetResolver.RequestedTarget);
        Assert.Equal(Target, services.RecordingController.CancelTarget);
    }

    private sealed class TestServices
    {
        public TargetWindow Window { get; } = new(Target, Application, 123);

        public WindowBounds Bounds { get; init; } = new(0, 0, 640, 480);

        public List<string> Events { get; } = [];

        public TestTargetResolver TargetResolver { get; private set; } = null!;

        public TestWindowController WindowController { get; private set; } = null!;

        public TestInputInjector InputInjector { get; private set; } = null!;

        public TestCursorMover CursorMover { get; private set; } = null!;

        public TestKeyboardInputInjector KeyboardInputInjector { get; private set; } = null!;

        public TestScreenshotCapture ScreenshotCapture { get; private set; } = null!;

        public TestRecordingController RecordingController { get; private set; } = null!;

        public CommandRunner CreateRunner()
        {
            TargetResolver = new TestTargetResolver(Window);
            WindowController = new TestWindowController(Bounds, Events);
            InputInjector = new TestInputInjector(Events);
            CursorMover = new TestCursorMover(Events);
            KeyboardInputInjector = new TestKeyboardInputInjector(Events);
            ScreenshotCapture = new TestScreenshotCapture();
            RecordingController = new TestRecordingController();

            return new CommandRunner(
                TargetResolver,
                WindowController,
                InputInjector,
                CursorMover,
                KeyboardInputInjector,
                ScreenshotCapture,
                RecordingController);
        }
    }

    private sealed class TestTargetResolver : ITargetResolver
    {
        private readonly TargetWindow window;

        public TestTargetResolver(TargetWindow window)
        {
            this.window = window;
        }

        public TargetConfiguration? RequestedTarget { get; private set; }

        public Task<TargetWindow> ResolveAsync(TargetConfiguration target, CancellationToken cancellationToken)
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

        public TargetWindow? ForegroundWindow { get; private set; }

        public (int Width, int Height)? Resize { get; private set; }

        public Task BringToForegroundAsync(TargetWindow window, CancellationToken cancellationToken)
        {
            ForegroundWindow = window;
            events.Add("foreground");
            return Task.CompletedTask;
        }

        public Task<WindowBounds> GetBoundsAsync(TargetWindow window, CancellationToken cancellationToken)
        {
            events.Add("bounds");
            return Task.FromResult(bounds);
        }

        public Task ResizeAsync(TargetWindow window, int width, int height, CancellationToken cancellationToken)
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

        public Task ClickAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
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

        public Task TypeAsync(TargetWindow window, IReadOnlyList<KeyboardAction> actions, CancellationToken cancellationToken)
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

        public Task MoveToAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
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

        public string? Caption { get; private set; }

        public Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CancellationToken cancellationToken)
        {
            OutputPath = outputPath;
            IncludeCursor = includeCursor;
            Caption = caption;
            return Task.CompletedTask;
        }
    }

    private sealed class TestRecordingController : IRecordingController
    {
        public TargetWindow? StartWindow { get; private set; }

        public string? OutputPath { get; private set; }

        public TimeSpan? TimeLimit { get; private set; }

        public TargetConfiguration? StopTarget { get; private set; }

        public TargetConfiguration? CancelTarget { get; private set; }

        public Task StartAsync(TargetWindow window, string outputPath, TimeSpan timeLimit, CancellationToken cancellationToken)
        {
            StartWindow = window;
            OutputPath = outputPath;
            TimeLimit = timeLimit;
            return Task.CompletedTask;
        }

        public Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken)
        {
            StopTarget = target;
            return Task.CompletedTask;
        }

        public Task CancelAsync(TargetConfiguration target, CancellationToken cancellationToken)
        {
            CancelTarget = target;
            return Task.CompletedTask;
        }
    }
}
