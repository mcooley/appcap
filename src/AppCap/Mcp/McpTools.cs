using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AppCap;

[McpServerToolType]
internal sealed class McpTools
{
    private readonly TargetCatalog catalog;
    private readonly McpCommandExecutor executor;

    public McpTools(TargetCatalog catalog, McpCommandExecutor executor)
    {
        this.catalog = catalog;
        this.executor = executor;
    }

    [McpServerTool(Name = "target_attach", OpenWorld = false), Description("Attaches a configured target.")]
    public Task<string> TargetAttachAsync(
        [Description("Configured target name. Auto-selects a running target when omitted.")] string? target = null,
        [Description("Launches the target application when true.")] bool launch = true,
        CancellationToken cancellationToken = default) =>
        RunAsync(new TargetAttachCommand(ResolveTarget(target), launch), cancellationToken);

    [McpServerTool(Name = "target_launch", OpenWorld = false), Description("Launches an attached target application.")]
    public Task<string> TargetLaunchAsync(
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new TargetLaunchCommand(ResolveTarget(target)), cancellationToken);

    [McpServerTool(Name = "target_detach", OpenWorld = false), Description("Detaches a configured target.")]
    public Task<string> TargetDetachAsync(
        [Description("Configured target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new TargetDetachCommand(ResolveTarget(target)), cancellationToken);

    [McpServerTool(Name = "target_list", ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Lists configured targets and their attachment and running state.")]
    public Task<string> TargetListAsync(CancellationToken cancellationToken = default) =>
        RunAsync(new TargetListCommand(), cancellationToken);

    [McpServerTool(Name = "inputdevice_attach", OpenWorld = false), Description("Attaches an input device to a configured target application.")]
    public Task<string> InputDeviceAttachAsync(
        [Description("Input device identifier, such as touch or keyboard. Use inputdevice_list to find available devices.")] string device,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new InputDeviceAttachCommand(ResolveTarget(target), ParseDevice(device)), cancellationToken);

    [McpServerTool(Name = "inputdevice_remove", OpenWorld = false), Description("Removes an attached input device from a configured target application.")]
    public Task<string> InputDeviceRemoveAsync(
        [Description("Input device identifier, such as touch or keyboard.")] string device,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new InputDeviceRemoveCommand(ResolveTarget(target), ParseDevice(device)), cancellationToken);

    [McpServerTool(Name = "inputdevice_list", ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Lists the target's supported input devices and attachment state.")]
    public Task<string> InputDeviceListAsync(
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new InputDeviceListCommand(ResolveTarget(target)), cancellationToken);

    [McpServerTool(Name = "tap", OpenWorld = false), Description("Injects a touch tap at pixel coordinates relative to the top-left of the target window.")]
    public Task<string> TapAsync(
        [Description("Horizontal coordinate in pixels.")] int x,
        [Description("Vertical coordinate in pixels.")] int y,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        [Description("Optional input device identifier. Attaches and uses touch when omitted.")] string? device = null,
        CancellationToken cancellationToken = default)
    {
        if (x < 0 || y < 0)
        {
            throw new McpException("Tap coordinates must be nonnegative.");
        }

        return RunAsync(new TapCommand(ResolveTarget(target), x, y, ParseOptionalDevice(device)), cancellationToken);
    }

    [McpServerTool(Name = "mouseto", OpenWorld = false), Description("Moves the mouse cursor to pixel coordinates relative to the top-left of the target window.")]
    public Task<string> MouseMoveAsync(
        [Description("Horizontal coordinate in pixels.")] int x,
        [Description("Vertical coordinate in pixels.")] int y,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        [Description("Optional input device identifier. Attaches and uses mouse when omitted.")] string? device = null,
        CancellationToken cancellationToken = default)
    {
        if (x < 0 || y < 0)
        {
            throw new McpException("Mouse coordinates must be nonnegative.");
        }

        return RunAsync(new MouseMoveCommand(ResolveTarget(target), x, y, ParseOptionalDevice(device)), cancellationToken);
    }

    [McpServerTool(Name = "click", OpenWorld = false), Description("Moves the mouse cursor and performs a primary click at pixel coordinates relative to the top-left of the target window.")]
    public Task<string> MouseClickAsync(
        [Description("Horizontal coordinate in pixels.")] int x,
        [Description("Vertical coordinate in pixels.")] int y,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        [Description("Optional input device identifier. Attaches and uses mouse when omitted.")] string? device = null,
        CancellationToken cancellationToken = default)
    {
        if (x < 0 || y < 0)
        {
            throw new McpException("Mouse coordinates must be nonnegative.");
        }

        return RunAsync(new MouseClickCommand(ResolveTarget(target), x, y, ParseOptionalDevice(device)), cancellationToken);
    }

    [McpServerTool(Name = "type", OpenWorld = false), Description("Types text into the target window.")]
    public Task<string> TypeAsync(
        [Description("Literal text and WebDriver/Playwright-style bracketed keys, for example hello[Enter] or [Control+A].")] string textAndKeys,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        [Description("Optional input device identifier. Attaches and uses keyboard when omitted.")] string? device = null,
        CancellationToken cancellationToken = default)
    {
        if (!KeyboardSequenceParser.TryParse(textAndKeys, out IReadOnlyList<KeyboardAction> actions, out string? errorMessage))
        {
            throw new McpException(errorMessage ?? "Invalid keyboard sequence.");
        }

        return RunAsync(new TypeCommand(ResolveTarget(target), textAndKeys, actions, ParseOptionalDevice(device)), cancellationToken);
    }

    [McpServerTool(Name = "resize", Idempotent = true, OpenWorld = false), Description("Resizes the target window.")]
    public Task<string> ResizeAsync(
        [Description("Window width in pixels.")] int width,
        [Description("Window height in pixels.")] int height,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0)
        {
            throw new McpException("Resize width and height must be positive.");
        }

        return RunAsync(new ResizeCommand(ResolveTarget(target), width, height), cancellationToken);
    }

    [McpServerTool(Name = "screenshot", ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Takes a PNG screenshot of the target window. Returns MCP image content unless outputPath is specified.")]
    public async Task<ContentBlock> ScreenshotAsync(
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        [Description("When set, writes the PNG to this path and returns the path instead of image content.")] string? outputPath = null,
        [Description("Excludes the cursor from the screenshot when true.")] bool excludeCursor = false,
        [Description("Optional caption text rendered over the screenshot.")] string? caption = null,
        [Description("Optional crop rectangle in target-window pixels.")] CropRectangle? crop = null,
        CancellationToken cancellationToken = default)
    {
        bool returnImage = string.IsNullOrWhiteSpace(outputPath);
        string capturePath = returnImage
            ? Path.Combine(Path.GetTempPath(), $"appcap-{Guid.NewGuid():N}.png")
            : ValidateExtension(outputPath!, ".png", "screenshot");

        try
        {
            await RunAsync(
                new ScreenshotCommand(ResolveTarget(target), capturePath, excludeCursor, NormalizeOptionalText(caption), crop),
                cancellationToken).ConfigureAwait(false);

            if (!returnImage)
            {
                return new TextContentBlock { Text = capturePath };
            }

            byte[] image = await File.ReadAllBytesAsync(capturePath, cancellationToken).ConfigureAwait(false);
            return ImageContentBlock.FromBytes(image, "image/png");
        }
        finally
        {
            if (returnImage)
            {
                File.Delete(capturePath);
            }
        }
    }

    [McpServerTool(Name = "record_start", OpenWorld = false), Description("Starts recording the target window to an MP4 file.")]
    public Task<string> RecordStartAsync(
        [Description("Output MP4 file path.")] string outputPath,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        [Description("Recording time limit in minutes. Fractional minutes are supported.")] double timeLimitMinutes = 30,
        [Description("Excludes the cursor from the recording when true.")] bool excludeCursor = false,
        [Description("Optional crop rectangle in target-window pixels.")] CropRectangle? crop = null,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(timeLimitMinutes) || timeLimitMinutes <= 0)
        {
            throw new McpException("Recording timeLimitMinutes must be positive and finite.");
        }

        return RunAsync(
            new RecordStartCommand(
                ResolveTarget(target),
                ValidateExtension(outputPath, ".mp4", "recording"),
                TimeSpan.FromMinutes(timeLimitMinutes),
                excludeCursor,
                crop),
            cancellationToken);
    }

    [McpServerTool(Name = "record_caption", OpenWorld = false), Description("Shows a caption in the active recording for three seconds.")]
    public Task<string> RecordCaptionAsync(
        [Description("Caption text to show.")] string text,
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new McpException("Caption text must not be empty.");
        }

        return RunAsync(new RecordCaptionCommand(ResolveTarget(target), text), cancellationToken);
    }

    [McpServerTool(Name = "record_stop", OpenWorld = false), Description("Stops and saves the active recording for a target application.")]
    public Task<string> RecordStopAsync(
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new RecordStopCommand(ResolveTarget(target)), cancellationToken);

    [McpServerTool(Name = "record_cancel", Destructive = true, OpenWorld = false), Description("Stops the active recording and discards its output file.")]
    public Task<string> RecordCancelAsync(
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new RecordCancelCommand(ResolveTarget(target)), cancellationToken);

    [McpServerTool(Name = "record_status", ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Reports the current or most recent recording state for an attached target.")]
    public Task<string> RecordStatusAsync(
        [Description("Attached target name. Uses the only attached target when omitted.")] string? target = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(new RecordStatusCommand(ResolveTarget(target)), cancellationToken);

    private async Task<string> RunAsync(AppCapCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await executor.RunAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (AppCapException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    private TargetApplication? ResolveTarget(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (catalog.TryParse(name, out TargetApplication target))
        {
            return target;
        }

        throw new McpException($"Unknown target '{name}'.");
    }

    private static InputDeviceType ParseDevice(string value)
    {
        if (InputDeviceType.TryParse(value, out InputDeviceType device))
        {
            return device;
        }

        throw new McpException($"Invalid input device identifier '{value}'.");
    }

    private static InputDeviceType? ParseOptionalDevice(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseDevice(value);

    private static string ValidateExtension(string path, string extension, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"The {description} output path must be a {extension} file.");
        }

        return path;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}