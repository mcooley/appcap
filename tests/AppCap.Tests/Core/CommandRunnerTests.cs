namespace AppCap.Tests;

public sealed class CommandRunnerTests
{
    private static readonly TargetApplication Target = new() { Name = "target", Id = "Package_family!App" };

    [Fact]
    public async Task InputDeviceAttachDelegatesToInputController()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new InputDeviceAttachCommand(Target, InputDeviceType.Touch), CancellationToken.None);

        Assert.Equal((Target, InputDeviceType.Touch), services.InputController.Attach);
        Assert.Null(services.TargetResolver.RequestedTarget);
    }

    [Fact]
    public async Task InputDeviceListReturnsAttachmentState()
    {
        TestServices services = new()
        {
            ListedDevices = new InputDeviceStatus[]
            {
                new InputDeviceStatus(InputDeviceType.Touch, true),
                new InputDeviceStatus(InputDeviceType.Keyboard, false),
            },
        };
        CommandRunner runner = services.CreateRunner();

        CommandExecutionResult result = await runner.RunAsync(new InputDeviceListCommand(Target), CancellationToken.None);

        Assert.Equal(
            $"touch: attached{Environment.NewLine}keyboard: detached",
            result.Output);
        Assert.Null(services.TargetResolver.RequestedTarget);
    }

    [Fact]
    public async Task TapDelegatesCoordinatesAndDeviceSelection()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new TapCommand(Target, 99, 49, InputDeviceType.Touch), CancellationToken.None);

        Assert.Equal((Target, 99, 49, InputDeviceType.Touch), services.InputController.Tap);
        Assert.Null(services.TargetResolver.RequestedTarget);
    }

    [Fact]
    public async Task MouseCommandsDelegateCoordinatesAndDeviceSelection()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new MouseMoveCommand(Target, 80, 40, InputDeviceType.Mouse), CancellationToken.None);
        await runner.RunAsync(new MouseClickCommand(Target, 90, 50, InputDeviceType.Mouse), CancellationToken.None);

        Assert.Equal((Target, 80, 40, InputDeviceType.Mouse), services.InputController.MouseMove);
        Assert.Equal((Target, 90, 50, InputDeviceType.Mouse), services.InputController.MouseClick);
        Assert.Null(services.TargetResolver.RequestedTarget);
    }

    [Fact]
    public async Task TypeDelegatesRawTextAndDeviceSelection()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();
        KeyboardAction[] actions = [new TextKeyboardAction("hello"), new KeyPressKeyboardAction([], KeyboardKey.Enter)];

        await runner.RunAsync(new TypeCommand(Target, "hello[Enter]", actions, InputDeviceType.Keyboard), CancellationToken.None);

        Assert.Equal((Target, "hello[Enter]", InputDeviceType.Keyboard), services.InputController.Type);
        Assert.Null(services.TargetResolver.RequestedTarget);
    }

    [Fact]
    public async Task InputErrorsPropagateWithoutResolvingWindow()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();
        services.InputController.FailTapWith = new AppCapException("No 'touch' input device is attached for target 'target'.", ExitCodes.UsageError);

        AppCapException exception = await Assert.ThrowsAsync<AppCapException>(() =>
            runner.RunAsync(new TapCommand(Target, 10, 20), CancellationToken.None));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Null(services.TargetResolver.RequestedTarget);
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

        await runner.RunAsync(new ScreenshotCommand(Target, "test.png", ExcludeCursor: false, Caption: "Test caption", Crop: new CropRectangle(10, 20, 300, 200)), CancellationToken.None);

        Assert.Equal("test.png", services.ScreenshotCapture.OutputPath);
        Assert.True(services.ScreenshotCapture.IncludeCursor);
        Assert.Equal("Test caption", services.ScreenshotCapture.Caption);
        Assert.Equal(new CropRectangle(10, 20, 300, 200), services.ScreenshotCapture.Crop);
    }

    [Fact]
    public async Task RecordStartResolvesTargetAndStartsRecording()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new RecordStartCommand(Target, "recording.mp4", TimeSpan.FromMinutes(45), ExcludeCursor: false, Crop: new CropRectangle(5, 6, 320, 240)), CancellationToken.None);

        Assert.Equal(Target, services.TargetResolver.RequestedTarget);
        Assert.Equal(services.Window, services.RecordingController.StartWindow);
        Assert.Equal("recording.mp4", services.RecordingController.OutputPath);
        Assert.Equal(TimeSpan.FromMinutes(45), services.RecordingController.TimeLimit);
        Assert.True(services.RecordingController.IncludeCursor);
        Assert.Equal(new CropRectangle(5, 6, 320, 240), services.RecordingController.Crop);
    }

    [Fact]
    public async Task RecordCaptionAddsCaptionWithoutResolvingWindow()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();

        await runner.RunAsync(new RecordCaptionCommand(Target, "Test caption"), CancellationToken.None);

        Assert.Null(services.TargetResolver.RequestedTarget);
        Assert.Equal(Target, services.RecordingController.CaptionTarget);
        Assert.Equal("Test caption", services.RecordingController.Caption);
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

    [Fact]
    public async Task RecordStatusFormatsLatestOutcome()
    {
        TestServices services = new();
        CommandRunner runner = services.CreateRunner();
        services.RecordingController.Status = new RecordingStatus("timed-out", @"C:\captures\test.mp4");

        CommandExecutionResult result = await runner.RunAsync(new RecordStatusCommand(Target), CancellationToken.None);

        Assert.Equal($"status: timed-out{Environment.NewLine}output: C:\\captures\\test.mp4", result.Output);
    }

    private sealed class TestServices
    {
        public TargetWindow Window { get; } = new(Target, 123);

        public WindowBounds Bounds { get; init; } = new(0, 0, 640, 480);

        public IReadOnlyList<InputDeviceStatus> ListedDevices { get; init; } = [];

        public TestTargetResolver TargetResolver { get; private set; } = null!;

        public TestWindowController WindowController { get; private set; } = null!;

        public TestInputController InputController { get; private set; } = null!;

        public TestScreenshotCapture ScreenshotCapture { get; private set; } = null!;

        public TestRecordingController RecordingController { get; private set; } = null!;

        public CommandRunner CreateRunner()
        {
            TargetResolver = new TestTargetResolver(Window);
            WindowController = new TestWindowController(Bounds);
            InputController = new TestInputController(ListedDevices);
            ScreenshotCapture = new TestScreenshotCapture();
            RecordingController = new TestRecordingController();

            return new CommandRunner(
                TargetResolver,
                WindowController,
                InputController,
                ScreenshotCapture,
                RecordingController);
        }
    }

    private sealed class TestTargetResolver : ITargetResolver
    {
        private readonly TargetWindow window;

        public TestTargetResolver(TargetWindow window) => this.window = window;

        public TargetApplication? RequestedTarget { get; private set; }

        public Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken)
        {
            RequestedTarget = target;
            return Task.FromResult(window);
        }

        public Task<TargetWindow> ResolveRunningAsync(TargetApplication target, CancellationToken cancellationToken) =>
            ResolveAsync(target, cancellationToken);
    }

    private sealed class TestWindowController : IWindowController
    {
        private readonly WindowBounds bounds;

        public TestWindowController(WindowBounds bounds) => this.bounds = bounds;

        public TargetWindow? ForegroundWindow { get; private set; }

        public (int Width, int Height)? Resize { get; private set; }

        public Task BringToForegroundAsync(TargetWindow window, CancellationToken cancellationToken)
        {
            ForegroundWindow = window;
            return Task.CompletedTask;
        }

        public Task<WindowBounds> GetBoundsAsync(TargetWindow window, CancellationToken cancellationToken) =>
            Task.FromResult(bounds);

        public Task ResizeAsync(TargetWindow window, int width, int height, CancellationToken cancellationToken)
        {
            Resize = (width, height);
            return Task.CompletedTask;
        }
    }

    private sealed class TestInputController : IInputController
    {
        private readonly IReadOnlyList<InputDeviceStatus> listedDevices;

        public TestInputController(IReadOnlyList<InputDeviceStatus> listedDevices) => this.listedDevices = listedDevices;

        public (TargetApplication Target, InputDeviceType DeviceType)? Attach { get; private set; }

        public (TargetApplication Target, InputDeviceType DeviceType)? Remove { get; private set; }

        public (TargetApplication Target, int X, int Y, InputDeviceType? DeviceType)? Tap { get; private set; }

        public (TargetApplication Target, int X, int Y, InputDeviceType? DeviceType)? MouseMove { get; private set; }

        public (TargetApplication Target, int X, int Y, InputDeviceType? DeviceType)? MouseClick { get; private set; }

        public (TargetApplication Target, string TextAndKeys, InputDeviceType? DeviceType)? Type { get; private set; }

        public AppCapException? FailTapWith { get; set; }

        public Task AttachInputDeviceAsync(TargetApplication target, InputDeviceType deviceType, CancellationToken cancellationToken)
        {
            Attach = (target, deviceType);
            return Task.CompletedTask;
        }

        public Task RemoveInputDeviceAsync(TargetApplication target, InputDeviceType deviceType, CancellationToken cancellationToken)
        {
            Remove = (target, deviceType);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InputDeviceStatus>> ListInputDevicesAsync(TargetApplication target, CancellationToken cancellationToken) =>
            Task.FromResult(listedDevices);

        public Task TapAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
        {
            if (FailTapWith is not null)
            {
                throw FailTapWith;
            }

            Tap = (target, x, y, deviceType);
            return Task.CompletedTask;
        }

        public Task MoveMouseAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
        {
            MouseMove = (target, x, y, deviceType);
            return Task.CompletedTask;
        }

        public Task ClickMouseAsync(TargetApplication target, int x, int y, InputDeviceType? deviceType, CancellationToken cancellationToken)
        {
            MouseClick = (target, x, y, deviceType);
            return Task.CompletedTask;
        }

        public Task TypeAsync(TargetApplication target, string textAndKeys, InputDeviceType? deviceType, CancellationToken cancellationToken)
        {
            Type = (target, textAndKeys, deviceType);
            return Task.CompletedTask;
        }
    }

    private sealed class TestScreenshotCapture : IScreenshotCapture
    {
        public string? OutputPath { get; private set; }

        public bool? IncludeCursor { get; private set; }

        public string? Caption { get; private set; }

        public CropRectangle? Crop { get; private set; }

        public Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CropRectangle? crop, CancellationToken cancellationToken)
        {
            OutputPath = outputPath;
            IncludeCursor = includeCursor;
            Caption = caption;
            Crop = crop;
            return Task.CompletedTask;
        }
    }

    private sealed class TestRecordingController : IRecordingController
    {
        public RecordingStatus Status { get; set; } = new("never-started");

        public TargetWindow? StartWindow { get; private set; }

        public string? OutputPath { get; private set; }

        public TimeSpan? TimeLimit { get; private set; }

        public bool? IncludeCursor { get; private set; }

        public CropRectangle? Crop { get; private set; }

        public TargetApplication? StopTarget { get; private set; }

        public TargetApplication? CancelTarget { get; private set; }

        public TargetApplication? CaptionTarget { get; private set; }

        public string? Caption { get; private set; }

        public Task StartAsync(TargetWindow window, string outputPath, TimeSpan timeLimit, bool includeCursor, CropRectangle? crop, CancellationToken cancellationToken)
        {
            StartWindow = window;
            OutputPath = outputPath;
            TimeLimit = timeLimit;
            IncludeCursor = includeCursor;
            Crop = crop;
            return Task.CompletedTask;
        }

        public Task StopAsync(TargetApplication target, CancellationToken cancellationToken)
        {
            StopTarget = target;
            return Task.CompletedTask;
        }

        public Task CancelAsync(TargetApplication target, CancellationToken cancellationToken)
        {
            CancelTarget = target;
            return Task.CompletedTask;
        }

        public Task AddCaptionAsync(TargetApplication target, string caption, CancellationToken cancellationToken)
        {
            CaptionTarget = target;
            Caption = caption;
            return Task.CompletedTask;
        }

        public Task<RecordingStatus> GetStatusAsync(TargetApplication target, CancellationToken cancellationToken) =>
            Task.FromResult(Status);
    }

}
