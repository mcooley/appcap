namespace RunMc.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task EmptyArgsRunsFocusCommandWithDefaultTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync([], runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        FocusCommand command = Assert.IsType<FocusCommand>(runner.Command);
        Assert.Equal(TargetKind.Default, command.Target);
    }

    [Fact]
    public async Task ClickParsesCoordinatesAndTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "runningbedrock", "click", "-x", "5", "-y", "7"],
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ClickCommand command = Assert.IsType<ClickCommand>(runner.Command);
        Assert.Equal(TargetKind.RunningBedrock, command.Target);
        Assert.Equal(5, command.X);
        Assert.Equal(7, command.Y);
    }

    [Fact]
    public async Task ResizeRequiresPositiveDimensions()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["resize", "--width", "0", "--height", "600"],
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
            ["--target", "runningbedrock", "hover", "-x", "5", "-y", "7"],
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        HoverCommand command = Assert.IsType<HoverCommand>(runner.Command);
        Assert.Equal(TargetKind.RunningBedrock, command.Target);
        Assert.Equal(5, command.X);
        Assert.Equal(7, command.Y);
    }

    [Fact]
    public async Task TypeParsesTextAndKeys()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "runningbedrock", "type", "hello[Enter]"],
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        TypeCommand command = Assert.IsType<TypeCommand>(runner.Command);
        Assert.Equal(TargetKind.RunningBedrock, command.Target);
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

        int exitCode = await CliApplication.RunAsync(["type", "--help"], runner, console);

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
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ScreenshotCommand command = Assert.IsType<ScreenshotCommand>(runner.Command);
        Assert.True(command.IncludeCursor);
        Assert.Equal("shot.png", command.OutputPath);
    }

    [Fact]
    public async Task HelpDoesNotRunCommand()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["screenshot", "--help"], runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("runmc screenshot", console.OutputText, StringComparison.Ordinal);
    }

    private sealed class RecordingRunner : IPhaseOneCommandRunner
    {
        public RunMcCommand? Command { get; private set; }

        public Task RunAsync(RunMcCommand command, CancellationToken cancellationToken)
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
