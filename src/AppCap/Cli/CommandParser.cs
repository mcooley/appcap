using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Globalization;

namespace AppCap;

public static class CommandParser
{
    private const string ConfigurationRequiredMessage = "Configuration must be loaded before commands can run.";

    public static bool CanInvokeWithoutConfiguration(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count is 0 ||
            args.Contains("--help", StringComparer.Ordinal) ||
            args.Contains("-?", StringComparer.Ordinal) ||
            args.Contains("--version", StringComparer.Ordinal) ||
            StartsWithDirective(args))
        {
            return true;
        }

        RootCommand rootCommand = CreateRootCommandForConfigurationlessInvocation();
        System.CommandLine.ParseResult result = rootCommand.Parse(args);
        return result.Errors.Count is 0 && result.CommandResult.Command == rootCommand;
    }

    public static System.CommandLine.ParseResult Parse(IReadOnlyList<string> args, TargetCatalog catalog, ICommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runner);

        return CreateRootCommand(catalog, runner).Parse(args);
    }

    internal static RootCommand CreateRootCommand(TargetCatalog catalog, ICommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runner);

        return CreateRootCommand(catalog, runner.RunAsync);
    }

    internal static RootCommand CreateRootCommandForConfigurationlessInvocation() =>
        CreateRootCommand(
            catalog: null,
            static (_, _) => Task.FromException(new AppCapException(ConfigurationRequiredMessage, ExitCodes.UsageError)));

    private static RootCommand CreateRootCommand(
        TargetCatalog? catalog,
        Func<AppCapCommand, CancellationToken, Task> executeCommandAsync)
    {
        ArgumentNullException.ThrowIfNull(executeCommandAsync);

        RootCommand rootCommand = new("Automates interactions with a configured target application.");
        HelpOption helpOption = rootCommand.Options.OfType<HelpOption>().Single();
        helpOption.Aliases.Remove("-h");
        Option<string?> targetOption = new("--target")
        {
            Description = catalog is null
                ? "Selects the configured target application."
                : $"Selects the configured target application. Defaults to {catalog.Default.Name}.",
            HelpName = "target",
            Recursive = true,
        };
        if (catalog is not null)
        {
            targetOption.CompletionSources.Add(_ => catalog.Applications.Select(application => application.Name));
            targetOption.Validators.Add(result =>
            {
                if (result.Tokens.Count is 0)
                {
                    return;
                }

                CommandResult? commandResult = FindParentCommandResult(result);
                if (commandResult is null)
                {
                    return;
                }

                CommandResult invokedCommand = FindInvokedCommandResult(commandResult);
                if (invokedCommand.Command == rootCommand || HasBuiltInShortCircuitToken(commandResult))
                {
                    return;
                }

                string? targetValue = result.GetValueOrDefault<string?>();
                if (targetValue is not null && !catalog.TryParse(targetValue, out _))
                {
                    result.AddError($"Unknown target '{targetValue}'.");
                }
            });
        }

        Option<int> coordinateXOption = RequiredIntegerOption(
            "-x",
            "pixels",
            "Sets the horizontal coordinate in pixels within the target window.",
            value => value >= 0);
        Option<int> coordinateYOption = RequiredIntegerOption(
            "-y",
            "pixels",
            "Sets the vertical coordinate in pixels within the target window.",
            value => value >= 0);
        Option<bool> excludeCursorOption = new("--exclude-cursor")
        {
            Description = "Excludes the cursor from the captured output.",
        };
        Option<CropRectangle?> cropOption = new("--crop")
        {
            Description = "Captures only the specified rectangle as x,y,width,height.",
            HelpName = "x,y,width,height",
        };
        cropOption.CustomParser = ParseCrop;

        Command clickCommand = new("click", "Injects a mouse click into the target window.");
        clickCommand.Add(coordinateXOption);
        clickCommand.Add(coordinateYOption);
        clickCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new ClickCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(coordinateXOption),
                    parseResult.GetRequiredValue(coordinateYOption)),
                cancellationToken));

        Command hoverCommand = new("hover", "Moves the cursor over the target window.");
        hoverCommand.Add(coordinateXOption);
        hoverCommand.Add(coordinateYOption);
        hoverCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new HoverCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(coordinateXOption),
                    parseResult.GetRequiredValue(coordinateYOption)),
                cancellationToken));

        Argument<string> typeTextArgument = new("text-and-keys")
        {
            Description = "Specifies the text and bracketed key presses to inject into the target window.",
            HelpName = "text-and-keys",
        };
        typeTextArgument.Validators.Add(result =>
        {
            string? sequence = result.GetValueOrDefault<string>();
            if (sequence is null)
            {
                return;
            }

            if (!KeyboardSequenceParser.TryParse(sequence, out _, out string? errorMessage))
            {
                result.AddError(errorMessage ?? "Invalid keyboard sequence.");
            }
        });
        Command typeCommand = new(
            "type",
            "Injects keyboard input into the target window. Bracketed keys use WebDriver/Playwright-style key names such as [Escape], [Enter], [Shift+F2], and [Control+A].");
        typeCommand.Add(typeTextArgument);
        typeCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new TypeCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    ParseKeyboardSequence(parseResult.GetRequiredValue(typeTextArgument))),
                cancellationToken));

        Option<int> resizeWidthOption = RequiredIntegerOption(
            "--width",
            "pixels",
            "Sets the target window width in pixels.",
            value => value > 0,
            "-w");
        Option<int> resizeHeightOption = RequiredIntegerOption(
            "--height",
            "pixels",
            "Sets the target window height in pixels.",
            value => value > 0,
            "-h");
        Command resizeCommand = new("resize", "Resizes the target window.");
        resizeCommand.Add(resizeWidthOption);
        resizeCommand.Add(resizeHeightOption);
        resizeCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new ResizeCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(resizeWidthOption),
                    parseResult.GetRequiredValue(resizeHeightOption)),
                cancellationToken));

        Option<string> screenshotOutputOption = RequiredOutputOption(
            ".png",
            "Writes the screenshot to the specified PNG file.",
            "screenshot output must be a .png file.");
        Option<string?> screenshotCaptionOption = new("--caption")
        {
            Description = "Shows a caption in the screenshot when specified.",
            HelpName = "text",
        };
        Command screenshotCommand = new("screenshot", "Takes a PNG screenshot of the target window.");
        screenshotCommand.Add(screenshotOutputOption);
        screenshotCommand.Add(excludeCursorOption);
        screenshotCommand.Add(screenshotCaptionOption);
        screenshotCommand.Add(cropOption);
        screenshotCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new ScreenshotCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(screenshotOutputOption),
                    parseResult.GetValue(excludeCursorOption),
                    NormalizeOptionalText(parseResult.GetValue(screenshotCaptionOption)),
                    parseResult.GetValue(cropOption)),
                cancellationToken));

        Option<string> recordOutputOption = RequiredOutputOption(
            ".mp4",
            "Writes the recording to the specified MP4 file.",
            "recording output must be a .mp4 file.");
        Option<TimeSpan> recordTimeLimitOption = new("--time-limit")
        {
            Description = "Sets the recording time limit in minutes. Fractional minutes are supported and the default is 30 minutes.",
            HelpName = "minutes",
            DefaultValueFactory = _ => TimeSpan.FromMinutes(30),
        };
        recordTimeLimitOption.CustomParser = ParseTimeLimit;
        Command recordStartCommand = new("start", "Starts recording the target window.");
        recordStartCommand.Add(recordOutputOption);
        recordStartCommand.Add(recordTimeLimitOption);
        recordStartCommand.Add(excludeCursorOption);
        recordStartCommand.Add(cropOption);
        recordStartCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new RecordStartCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(recordOutputOption),
                    parseResult.GetValue(recordTimeLimitOption),
                    parseResult.GetValue(excludeCursorOption),
                    parseResult.GetValue(cropOption)),
                cancellationToken));

        Command recordStopCommand = new("stop", "Stops recording the target window.");
        recordStopCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new RecordStopCommand(ResolveTarget(parseResult, targetOption, catalog)),
                cancellationToken));

        Command recordCancelCommand = new("cancel", "Stops recording the target window and discards the output file.");
        recordCancelCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new RecordCancelCommand(ResolveTarget(parseResult, targetOption, catalog)),
                cancellationToken));

        Argument<string> recordCaptionArgument = new("text")
        {
            Description = "Specifies the caption text to show in the active recording.",
            HelpName = "text",
        };
        recordCaptionArgument.Validators.Add(result =>
        {
            string? caption = result.GetValueOrDefault<string>();
            if (caption is not null && string.IsNullOrWhiteSpace(caption))
            {
                result.AddError("Caption text must not be empty.");
            }
        });
        Command recordCaptionCommand = new("caption", "Shows a caption in the recording for three seconds.");
        recordCaptionCommand.Add(recordCaptionArgument);
        recordCaptionCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new RecordCaptionCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(recordCaptionArgument)),
                cancellationToken));

        Command recordCommand = new(
            "record",
            "Starts, stops, or cancels recording the target window. The cursor is included by default. Captions fade out after 3 seconds. Recordings stop and save after 30 minutes by default.");
        recordCommand.Add(recordStartCommand);
        recordCommand.Add(recordStopCommand);
        recordCommand.Add(recordCancelCommand);
        recordCommand.Add(recordCaptionCommand);

        rootCommand.SetAction(parseResult => new HelpAction().Invoke(parseResult));
        rootCommand.Add(targetOption);
        rootCommand.Add(clickCommand);
        rootCommand.Add(hoverCommand);
        rootCommand.Add(typeCommand);
        rootCommand.Add(resizeCommand);
        rootCommand.Add(screenshotCommand);
        rootCommand.Add(recordCommand);
        return rootCommand;
    }

    internal static bool StartsWithDirective(IReadOnlyList<string> args) =>
        args.Count > 0 && IsDirectiveToken(args[0]);

    private static bool IsDirectiveToken(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= 3 &&
        value[0] == '[' &&
        value[^1] == ']';

    private static CommandResult? FindParentCommandResult(SymbolResult result)
    {
        for (SymbolResult? current = result.Parent; current is not null; current = current.Parent)
        {
            if (current is CommandResult commandResult)
            {
                return commandResult;
            }
        }

        return null;
    }

    private static bool HasBuiltInShortCircuitToken(CommandResult commandResult) =>
        commandResult.Tokens.Any(token => token.Value is "--help" or "-?" or "--version");

    private static CommandResult FindInvokedCommandResult(CommandResult commandResult)
    {
        CommandResult current = commandResult;
        while (current.Children.OfType<CommandResult>().FirstOrDefault() is CommandResult childCommand)
        {
            current = childCommand;
        }

        return current;
    }

    private static Option<int> RequiredIntegerOption(
        string name,
        string helpName,
        string description,
        Func<int, bool> isValid,
        params string[] aliases)
    {
        Option<int> option = new(name, aliases)
        {
            Description = description,
            HelpName = helpName,
            Required = true,
        };
        option.Validators.Add(result =>
        {
            if (result.Tokens.Count is not 1 || !int.TryParse(result.Tokens[0].Value, out int value))
            {
                return;
            }

            if (!isValid(value))
            {
                result.AddError($"Invalid value for {name}.");
            }
        });
        return option;
    }

    private static Option<string> RequiredOutputOption(string extension, string description, string extensionErrorMessage)
    {
        Option<string> option = new("--output")
        {
            Description = description,
            HelpName = $"path{extension}",
            Required = true,
        };
        option.Validators.Add(result =>
        {
            if (result.Tokens.Count is 0)
            {
                return;
            }

            string? value = result.GetValueOrDefault<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("Invalid value for --output.");
                return;
            }

            if (!Path.GetExtension(value).Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(extensionErrorMessage);
            }
        });
        return option;
    }

    private static IReadOnlyList<KeyboardAction> ParseKeyboardSequence(string sequence)
    {
        if (!KeyboardSequenceParser.TryParse(sequence, out IReadOnlyList<KeyboardAction> actions, out string? errorMessage))
        {
            throw new AppCapException(errorMessage ?? "Invalid keyboard sequence.", ExitCodes.UsageError);
        }

        return actions;
    }

    private static TimeSpan ParseTimeLimit(ArgumentResult result)
    {
        string? value = result.Tokens.Count is 1 ? result.Tokens[0].Value : null;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) ||
            minutes < (1d / 60d) ||
            minutes > (int.MaxValue / 60d))
        {
            result.AddError("Invalid value for --time-limit.");
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static CropRectangle? ParseCrop(ArgumentResult result)
    {
        if (result.Tokens.Count is 0)
        {
            return null;
        }

        string? value = result.Tokens.Count is 1 ? result.Tokens[0].Value : null;
        if (!CropRectangle.TryParse(value, out CropRectangle crop))
        {
            result.AddError("Invalid value for --crop. Expected x,y,width,height with nonnegative x/y and positive width/height.");
            return null;
        }

        return crop;
    }

    private static TargetApplication ResolveTarget(
        System.CommandLine.ParseResult parseResult,
        Option<string?> targetOption,
        TargetCatalog? catalog)
    {
        if (catalog is null)
        {
            throw new AppCapException(ConfigurationRequiredMessage, ExitCodes.UsageError);
        }

        string? targetValue = parseResult.GetValue(targetOption);
        return targetValue is not null && catalog.TryParse(targetValue, out TargetApplication target)
            ? target
            : catalog.Default;
    }

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}