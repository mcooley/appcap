using System.CommandLine;

namespace AppCap.Tests;

public sealed class CliApplicationTests
{
    private static readonly TargetCatalog Catalog = new(
    [
        new TargetApplication { Name = "calculator", Id = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
        new TargetApplication { Name = "notepad", Id = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" },
        new TargetApplication { Name = "paint", Id = "Microsoft.Paint_8wekyb3d8bbwe!App" },
    ]);

    [Fact]
    public async Task EmptyArgsPrintsRootHelp()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync([], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Usage:", console.OutputText, StringComparison.Ordinal);
        Assert.Contains("Commands:", console.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TapParsesCoordinatesTargetAndDevice()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "calculator", "tap", "-x", "5", "-y", "7", "--device", "touch"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        TapCommand command = Assert.IsType<TapCommand>(runner.Command);
        Assert.Equal("calculator", command.Target.Name);
        Assert.Equal(5, command.X);
        Assert.Equal(7, command.Y);
        Assert.Equal(InputDeviceType.Touch, command.DeviceType);
    }

    [Fact]
    public async Task TargetOnlyArgsPrintRootHelp()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "paint"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Usage:", console.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyInvocationCanRunWithoutConfiguration()
    {
        Assert.True(CommandParser.CanInvokeWithoutConfiguration([]));
    }

    [Fact]
    public void TargetOnlyInvocationCanRunWithoutConfiguration()
    {
        Assert.True(CommandParser.CanInvokeWithoutConfiguration(["--target", "paint"]));
    }

    [Fact]
    public void InvocationWithVerbRequiresConfiguration()
    {
        Assert.False(CommandParser.CanInvokeWithoutConfiguration(["tap", "-x", "1", "-y", "2"]));
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("tap", "--help")]
    [InlineData("--version")]
    [InlineData("[suggest]", "tap")]
    public void BuiltInHelpVersionAndDirectivesCanRunWithoutConfiguration(params string[] args)
    {
        Assert.True(CommandParser.CanInvokeWithoutConfiguration(args));
    }

    [Fact]
    public async Task UnknownTargetWithoutVerbPrintsRootHelp()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "nope"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Usage:", console.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownTargetWithVerbReturnsUsageError()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "nope", "tap", "-x", "1", "-y", "1"],
            Catalog,
            runner,
            console);

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

    [Theory]
    [InlineData("notepad")]
    [InlineData("paint")]
    public async Task ParsesNamedTargets(string targetValue)
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", targetValue, "tap", "-x", "5", "-y", "7"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        TapCommand command = Assert.IsType<TapCommand>(runner.Command);
        Assert.Equal(targetValue, command.Target.Name);
    }

    [Fact]
    public async Task TypeParsesTextAndKeys()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "calculator", "type", "hello[Enter]"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        TypeCommand command = Assert.IsType<TypeCommand>(runner.Command);
        Assert.Equal("calculator", command.Target.Name);
        Assert.Equal("hello[Enter]", command.TextAndKeys);
        Assert.Collection(
            command.Actions,
            action => Assert.Equal("hello", Assert.IsType<TextKeyboardAction>(action).Text),
            action => Assert.Equal(KeyboardKey.Enter, Assert.IsType<KeyPressKeyboardAction>(action).Key));
    }

    [Fact]
    public async Task TypeParsesDeviceSelection()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["type", "abc", "--device", "keyboard"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        TypeCommand command = Assert.IsType<TypeCommand>(runner.Command);
        Assert.Equal(InputDeviceType.Keyboard, command.DeviceType);
    }

    [Fact]
    public async Task InputDeviceAttachParsesTargetAndDevice()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "paint", "inputdevice", "attach", "touch"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        InputDeviceAttachCommand command = Assert.IsType<InputDeviceAttachCommand>(runner.Command);
        Assert.Equal("paint", command.Target.Name);
        Assert.Equal(InputDeviceType.Touch, command.DeviceType);
    }

    [Fact]
    public async Task InputDeviceListParsesTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "paint", "inputdevice", "list"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        InputDeviceListCommand command = Assert.IsType<InputDeviceListCommand>(runner.Command);
        Assert.Equal("paint", command.Target.Name);
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
    public async Task ScreenshotIncludesCursorByDefault()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--output", "shot.png"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ScreenshotCommand command = Assert.IsType<ScreenshotCommand>(runner.Command);
        Assert.False(command.ExcludeCursor);
        Assert.Equal("shot.png", command.OutputPath);
    }

    [Fact]
    public async Task ScreenshotParsesExcludeCursor()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--exclude-cursor", "--output", "shot.png"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ScreenshotCommand command = Assert.IsType<ScreenshotCommand>(runner.Command);
        Assert.True(command.ExcludeCursor);
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
    public async Task ScreenshotParsesCrop()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--crop", "10,20,300,200", "--output", "shot.png"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        ScreenshotCommand command = Assert.IsType<ScreenshotCommand>(runner.Command);
        Assert.Equal(new CropRectangle(10, 20, 300, 200), command.Crop);
    }

    [Theory]
    [InlineData("-1,0,10,10")]
    [InlineData("0,-1,10,10")]
    [InlineData("0,0,0,10")]
    [InlineData("0,0,10,0")]
    [InlineData("0,0,10")]
    [InlineData("nope")]
    public async Task ScreenshotRejectsInvalidCrop(string crop)
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["screenshot", "--crop", crop, "--output", "shot.png"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Invalid value for --crop.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordStartParsesOutputPathAndTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "paint", "record", "start", "--output", "recording.mp4"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStartCommand command = Assert.IsType<RecordStartCommand>(runner.Command);
        Assert.Equal("paint", command.Target.Name);
        Assert.Equal("recording.mp4", command.OutputPath);
        Assert.Equal(TimeSpan.FromMinutes(30), command.TimeLimit);
        Assert.False(command.ExcludeCursor);
    }

    [Fact]
    public async Task RecordStartParsesExcludeCursor()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["record", "start", "--output", "recording.mp4", "--exclude-cursor"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStartCommand command = Assert.IsType<RecordStartCommand>(runner.Command);
        Assert.True(command.ExcludeCursor);
    }

    [Fact]
    public async Task RecordStartParsesCrop()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["record", "start", "--output", "recording.mp4", "--crop", "5,6,320,240"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStartCommand command = Assert.IsType<RecordStartCommand>(runner.Command);
        Assert.Equal(new CropRectangle(5, 6, 320, 240), command.Crop);
    }

    [Fact]
    public async Task RecordCaptionParsesTextAndTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["--target", "paint", "record", "caption", "Test caption"],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordCaptionCommand command = Assert.IsType<RecordCaptionCommand>(runner.Command);
        Assert.Equal("paint", command.Target.Name);
        Assert.Equal("Test caption", command.Caption);
    }

    [Fact]
    public async Task RecordCaptionRejectsWhitespaceText()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(
            ["record", "caption", "   "],
            Catalog,
            runner,
            console);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Caption text must not be empty.", console.ErrorText, StringComparison.Ordinal);
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

        int exitCode = await CliApplication.RunAsync(["--target", "paint", "record", "stop"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordStopCommand command = Assert.IsType<RecordStopCommand>(runner.Command);
        Assert.Equal("paint", command.Target.Name);
    }

    [Fact]
    public async Task RecordCancelParsesTarget()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "paint", "record", "cancel"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        RecordCancelCommand command = Assert.IsType<RecordCancelCommand>(runner.Command);
        Assert.Equal("paint", command.Target.Name);
    }

    [Fact]
    public async Task HelpDoesNotRunCommand()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["screenshot", "--help"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Contains("Usage:", console.OutputText, StringComparison.Ordinal);
        Assert.Contains("screenshot", console.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpWithUnknownTargetDoesNotRunCommand()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--target", "nope", "tap", "--help"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.DoesNotContain("Unknown target 'nope'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("tap", console.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionDoesNotRunCommand()
    {
        RecordingRunner runner = new();
        using TestConsole console = new();

        int exitCode = await CliApplication.RunAsync(["--version"], Catalog, runner, console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(runner.Command);
        Assert.Matches(@"\S", console.OutputText);
    }

    [Fact]
    public void TargetOptionOffersConfiguredCompletions()
    {
        RecordingRunner runner = new();
        RootCommand rootCommand = CommandParser.CreateRootCommand(Catalog, runner);

        System.CommandLine.ParseResult parseResult = rootCommand.Parse(["--target"]);
        string[] completions = parseResult.GetCompletions().Select(completion => completion.Label).ToArray();

        Assert.Contains("calculator", completions, StringComparer.Ordinal);
        Assert.Contains("notepad", completions, StringComparer.Ordinal);
        Assert.Contains("paint", completions, StringComparer.Ordinal);
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
