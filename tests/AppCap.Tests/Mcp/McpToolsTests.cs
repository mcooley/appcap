using ModelContextProtocol.Protocol;

namespace AppCap.Tests;

public sealed class McpToolsTests
{
    private static readonly TargetApplication Target = new() { Name = "target", Id = "Package_family!App" };

    [Fact]
    public async Task TapMapsArgumentsToCanonicalCommand()
    {
        TestRunner runner = new();
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        await tools.TapAsync(12, 34, device: "touch");

        TapCommand command = Assert.IsType<TapCommand>(runner.Command);
        Assert.Null(command.Target);
        Assert.Equal(12, command.X);
        Assert.Equal(34, command.Y);
        Assert.Equal(InputDeviceType.Touch, command.DeviceType);
    }

    [Fact]
    public async Task MouseToolsMapArgumentsToCanonicalCommands()
    {
        TestRunner runner = new();
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        await tools.MouseMoveAsync(12, 34, device: "mouse");
        MouseMoveCommand move = Assert.IsType<MouseMoveCommand>(runner.Command);
        Assert.Equal((12, 34, InputDeviceType.Mouse), (move.X, move.Y, move.DeviceType));

        await tools.MouseClickAsync(56, 78, device: "mouse");
        MouseClickCommand click = Assert.IsType<MouseClickCommand>(runner.Command);
        Assert.Equal((56, 78, InputDeviceType.Mouse), (click.X, click.Y, click.DeviceType));
    }

    [Fact]
    public async Task CommandOutputIsReturnedAsToolText()
    {
        TestRunner runner = new() { Result = new CommandExecutionResult("touch: attached") };
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        string result = await tools.InputDeviceListAsync();

        Assert.Equal("touch: attached", result);
    }

    [Fact]
    public async Task ScreenshotReturnsOfficialImageContentBlock()
    {
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10];
        TestRunner runner = new() { ScreenshotBytes = png };
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        ContentBlock result = await tools.ScreenshotAsync();

        ImageContentBlock image = Assert.IsType<ImageContentBlock>(result);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(png, image.DecodedData.ToArray());
        Assert.IsType<ScreenshotCommand>(runner.Command);
    }

    [Fact]
    public async Task RecordStartMapsCliTerminologyAndDefaults()
    {
        TestRunner runner = new();
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        await tools.RecordStartAsync("capture.mp4", timeLimitMinutes: 12.5, excludeCursor: true);

        RecordStartCommand command = Assert.IsType<RecordStartCommand>(runner.Command);
        Assert.Equal("capture.mp4", command.OutputPath);
        Assert.Equal(TimeSpan.FromMinutes(12.5), command.TimeLimit);
        Assert.True(command.ExcludeCursor);
        Assert.False(command.NoAudio);
    }

    [Fact]
    public async Task RecordStartMapsNoAudio()
    {
        TestRunner runner = new();
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        await tools.RecordStartAsync("capture.mp4", noAudio: true);

        Assert.True(Assert.IsType<RecordStartCommand>(runner.Command).NoAudio);
    }

    [Fact]
    public async Task TargetSessionToolsMapToCanonicalCommands()
    {
        TestRunner runner = new();
        using McpCommandExecutor executor = new(runner);
        McpTools tools = new(new TargetCatalog([Target]), executor);

        await tools.TargetAttachAsync("target", launch: false);
        TargetAttachCommand attach = Assert.IsType<TargetAttachCommand>(runner.Command);
        Assert.Equal(Target, attach.Target);
        Assert.False(attach.Launch);

        await tools.TargetLaunchAsync("target");
        Assert.Equal(Target, Assert.IsType<TargetLaunchCommand>(runner.Command).Target);

        await tools.TargetDetachAsync();
        Assert.Null(Assert.IsType<TargetDetachCommand>(runner.Command).Target);

        await tools.TargetListAsync();
        Assert.IsType<TargetListCommand>(runner.Command);

        await tools.RecordStatusAsync();
        Assert.IsType<RecordStatusCommand>(runner.Command);
    }

    private sealed class TestRunner : ICommandRunner
    {
        public AppCapCommand? Command { get; private set; }

        public byte[]? ScreenshotBytes { get; init; }

        public CommandExecutionResult Result { get; init; } = CommandExecutionResult.Empty;

        public async Task<CommandExecutionResult> RunAsync(AppCapCommand command, CancellationToken cancellationToken)
        {
            Command = command;
            if (command is ScreenshotCommand screenshot && ScreenshotBytes is not null)
            {
                await File.WriteAllBytesAsync(screenshot.OutputPath, ScreenshotBytes, cancellationToken);
            }

            return Result;
        }
    }
}