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
        Assert.Equal(Target, command.Target);
        Assert.Equal(12, command.X);
        Assert.Equal(34, command.Y);
        Assert.Equal(InputDeviceType.Touch, command.DeviceType);
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