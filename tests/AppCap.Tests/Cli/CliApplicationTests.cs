namespace AppCap.Tests;

public sealed class CliApplicationTests
{
    private static readonly TargetCatalog Catalog = new(
    [
        new AppCapTargetConfig { Name = "bedrock", Id = "Microsoft.MinecraftUWP_8wekyb3d8bbwe!Game" },
        new AppCapTargetConfig { Name = "bedrockpreview", Id = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe!Game" },
        new AppCapTargetConfig { Name = "education", Id = "Microsoft.MinecraftEducationEdition_8wekyb3d8bbwe!Microsoft.MinecraftEducationEdition" },
        new AppCapTargetConfig { Name = "testapp", Id = "AppCap.E2ETestApp_6tt0djjd38yk4!App" },
    ]);

    [Fact]
    public async Task EmptyArgsRunsFocusCommandWithDefaultTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync([], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        FocusCommand command = Assert.IsType<FocusCommand>(runner.Command);
        Assert.Equal("default", command.Target.Name);
        Assert.Equal(["bedrock", "bedrockpreview", "education", "testapp"], command.Target.Applications.Select(application => application.Name));
    }

    [Fact]
    public async Task ClickParsesCoordinatesAndTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "bedrock", "click", "-x", "5", "-y", "7"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ClickCommand command = Assert.IsType<ClickCommand>(runner.Command);
        Assert.Equal("bedrock", command.Target.Name);
        Assert.Single(command.Target.Applications);
        Assert.Equal(5, command.X);
        Assert.Equal(7, command.Y);
    }

    [Fact]
    public async Task TargetOnlyArgsRunFocusCommandWithSelectedTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "education"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        FocusCommand command = Assert.IsType<FocusCommand>(runner.Command);
        Assert.Equal("education", command.Target.Name);
        Assert.Single(command.Target.Applications);
    }

    [Fact]
    public async Task UnknownTargetReturnsUsageError()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "nope"], Catalog, runner, console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Unknown target 'nope'.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResizeRequiresPositiveDimensions()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["resize", "--width", "0", "--height", "600"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Invalid value for --width.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoverParsesCoordinatesAndTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "bedrock", "hover", "-x", "5", "-y", "7"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        HoverCommand command = Assert.IsType<HoverCommand>(runner.Command);
        Assert.Equal("bedrock", command.Target.Name);
        Assert.Equal(5, command.X);
        Assert.Equal(7, command.Y);
    }

    [Theory]
    [InlineData("bedrockpreview")]
    [InlineData("education")]
    [InlineData("testapp")]
    public async Task ParsesNamedTargets(string targetValue)
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", targetValue, "hover", "-x", "5", "-y", "7"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        HoverCommand command = Assert.IsType<HoverCommand>(runner.Command);
        Assert.Equal(targetValue, command.Target.Name);
        Assert.Single(command.Target.Applications);
    }

    [Fact]
    public async Task TypeParsesTextAndKeys()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "bedrock", "type", "hello[Enter]"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        TypeCommand command = Assert.IsType<TypeCommand>(runner.Command);
        Assert.Equal("bedrock", command.Target.Name);
        Assert.Collection(
            command.Actions,
            action => Assert.Equal("hello", Assert.IsType<TextKeyboardAction>(action).Text),
            action => Assert.Equal(KeyboardKey.Enter, Assert.IsType<KeyPressKeyboardAction>(action).Key));
    }

    [Fact]
    public async Task TypeHelpMentionsWebDriverPlaywrightStyleKeys()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["type", "--help"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("WebDriver/Playwright-style key names", console.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResizeParsesShortWidthAndHeightAliases()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["resize", "-w", "1024", "-h", "768"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ResizeCommand command = Assert.IsType<ResizeCommand>(runner.Command);
        Assert.Equal(1024, command.Width);
        Assert.Equal(768, command.Height);
    }

    [Fact]
    public async Task ScreenshotRequiresPngOutput()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--output", "shot.jpg"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("screenshot output must be a .png file.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScreenshotParsesIncludeCursor()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--include-cursor", "--output", "shot.png"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ScreenshotCommand command = Assert.IsType<ScreenshotCommand>(runner.Command);
        Assert.True(command.IncludeCursor);
        Assert.Equal("shot.png", command.OutputPath);
    }

    [Fact]
    public async Task ScreenshotParsesCaption()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--caption", "Test caption", "--output", "shot.png"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ScreenshotCommand command = Assert.IsType<ScreenshotCommand>(runner.Command);
        Assert.Equal("Test caption", command.Caption);
    }

    [Fact]
    public async Task RecordStartParsesOutputPathAndTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "testapp", "record", "start", "--output", "recording.mp4"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStartCommand command = Assert.IsType<RecordStartCommand>(runner.Command);
        Assert.Equal("testapp", command.Target.Name);
        Assert.Equal("recording.mp4", command.OutputPath);
        Assert.Equal(TimeSpan.FromMinutes(30), command.TimeLimit);
    }

    [Fact]
    public async Task RecordStartParsesFractionalTimeLimit()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["record", "start", "--output", "recording.mp4", "--time-limit", "0.05"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStartCommand command = Assert.IsType<RecordStartCommand>(runner.Command);
        Assert.Equal(TimeSpan.FromSeconds(3), command.TimeLimit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("nope")]
    public async Task RecordStartRejectsInvalidTimeLimit(string timeLimit)
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["record", "start", "--output", "recording.mp4", "--time-limit", timeLimit],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Invalid value for --time-limit.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordStartRequiresMp4Output()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["record", "start", "--output", "recording.avi"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("recording output must be a .mp4 file.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordStopParsesTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "testapp", "record", "stop"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStopCommand command = Assert.IsType<RecordStopCommand>(runner.Command);
        Assert.Equal("testapp", command.Target.Name);
    }

    [Fact]
    public async Task RecordCancelParsesTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "testapp", "record", "cancel"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordCancelCommand command = Assert.IsType<RecordCancelCommand>(runner.Command);
        Assert.Equal("testapp", command.Target.Name);
    }

    [Fact]
    public async Task HelpDoesNotRunCommand()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["screenshot", "--help"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("appcap screenshot", console.OutputText, StringComparison.Ordinal);
    }

    private sealed class RecordingRunner : ICommandRunner
    {
        public AppCapCommand? Command { get; private set; }

        public Task RunAsync(AppCapCommand command, CancellationToken cancellationToken)
        {
            Command = command;
            return Task.CompletedTask;
        }
    }

    private sealed class TestConsole : ICommandConsole, IDisposable
    {
        private readonly StringWriter output = new();
        private readonly StringWriter error = new();

        public TextWriter Output => output;

        public TextWriter ErrorOutput => error;

        public string OutputText => output.ToString();

        public string ErrorText => error.ToString();

        public void Dispose()
        {
            output.Dispose();
            error.Dispose();
        }
    }
}
