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

    internal static RootCommand CreateRootCommand(
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
                ? "Selects an attached target application."
                : "Selects an attached target application. Required when multiple targets are attached.",
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

        Option<int?> coordinateXOption = OptionalIntegerOption(
            "-x",
            "pixels",
            "Sets the horizontal coordinate in pixels within the target window.",
            value => value >= 0);
        Option<int?> coordinateYOption = OptionalIntegerOption(
            "-y",
            "pixels",
            "Sets the vertical coordinate in pixels within the target window.",
            value => value >= 0);
        Argument<CoordinatePair?> coordinatesArgument = new("coordinates")
        {
            Description = "Specifies the coordinates as x,y.",
            HelpName = "x,y",
        };
        coordinatesArgument.CustomParser = ParseCoordinates;
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

        Option<string?> deviceOption = CreateDeviceOption();
        Argument<string> inputDeviceArgument = CreateInputDeviceArgument();

        Argument<string?> targetNameArgument = new("name")
        {
            Description = "Specifies a configured target name.",
            HelpName = "name",
            Arity = ArgumentArity.ZeroOrOne,
        };
        if (catalog is not null)
        {
            targetNameArgument.CompletionSources.Add(_ => catalog.Applications.Select(application => application.Name));
            targetNameArgument.Validators.Add(result => ValidateTargetName(result, catalog));
        }

        Option<bool> noLaunchOption = new("--no-launch")
        {
            Description = "Attaches the target without launching its application.",
        };
        Command targetAttachCommand = new("attach", "Attaches a configured target.");
        targetAttachCommand.Add(targetNameArgument);
        targetAttachCommand.Add(noLaunchOption);
        targetAttachCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new TargetAttachCommand(
                    ResolveOptionalTarget(parseResult.GetValue(targetNameArgument), catalog),
                    !parseResult.GetValue(noLaunchOption)),
                cancellationToken));

        Argument<string?> launchTargetNameArgument = new("name")
        {
            Description = "Specifies a configured target name.",
            HelpName = "name",
            Arity = ArgumentArity.ZeroOrOne,
        };
        if (catalog is not null)
        {
            launchTargetNameArgument.CompletionSources.Add(_ => catalog.Applications.Select(application => application.Name));
            launchTargetNameArgument.Validators.Add(result => ValidateTargetName(result, catalog));
        }

        Command targetLaunchCommand = new("launch", "Launches an attached target application.");
        targetLaunchCommand.Add(launchTargetNameArgument);
        targetLaunchCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new TargetLaunchCommand(ResolveOptionalTarget(parseResult.GetValue(launchTargetNameArgument), catalog)),
                cancellationToken));

        Argument<string?> detachTargetNameArgument = new("name")
        {
            Description = "Specifies a configured target name.",
            HelpName = "name",
            Arity = ArgumentArity.ZeroOrOne,
        };
        if (catalog is not null)
        {
            detachTargetNameArgument.CompletionSources.Add(_ => catalog.Applications.Select(application => application.Name));
            detachTargetNameArgument.Validators.Add(result => ValidateTargetName(result, catalog));
        }

        Command targetDetachCommand = new("detach", "Detaches a configured target.");
        targetDetachCommand.Add(detachTargetNameArgument);
        targetDetachCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new TargetDetachCommand(ResolveOptionalTarget(parseResult.GetValue(detachTargetNameArgument), catalog)),
                cancellationToken));

        Command targetListCommand = new("list", "Lists configured targets and their attachment and running state.");
        targetListCommand.SetAction((_, cancellationToken) => executeCommandAsync(new TargetListCommand(), cancellationToken));

        Command targetCommand = new("target", "Manages target sessions.");
        targetCommand.Add(targetAttachCommand);
        targetCommand.Add(targetLaunchCommand);
        targetCommand.Add(targetDetachCommand);
        targetCommand.Add(targetListCommand);

        Command tapCommand = new("tap", "Injects a tap into the target window.");
        tapCommand.Add(coordinatesArgument);
        tapCommand.Add(coordinateXOption);
        tapCommand.Add(coordinateYOption);
        tapCommand.Add(deviceOption);
        tapCommand.Validators.Add(result =>
        {
            bool hasCoordinates = result.GetResult(coordinatesArgument)?.Tokens.Count > 0;
            bool hasCoordinateX = result.GetResult(coordinateXOption)?.Tokens.Count > 0;
            bool hasCoordinateY = result.GetResult(coordinateYOption)?.Tokens.Count > 0;

            if (hasCoordinates && (hasCoordinateX || hasCoordinateY))
            {
                result.AddError("Specify coordinates either as x,y or with -x and -y, not both.");
            }
            else if (!hasCoordinates && (!hasCoordinateX || !hasCoordinateY))
            {
                result.AddError("Coordinates are required. Specify x,y or both -x and -y.");
            }
        });
        tapCommand.SetAction((parseResult, cancellationToken) =>
        {
            CoordinatePair? coordinates = parseResult.GetValue(coordinatesArgument);
            return executeCommandAsync(
                new TapCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    coordinates?.X ?? parseResult.GetValue(coordinateXOption) ?? throw MissingCoordinatesException(),
                    coordinates?.Y ?? parseResult.GetValue(coordinateYOption) ?? throw MissingCoordinatesException(),
                    ParseOptionalDeviceType(parseResult.GetValue(deviceOption))),
                cancellationToken);
        });

        Command mouseMoveCommand = CreatePointerCommand(
            "mouseto",
            "Moves the mouse cursor within the target window.",
            static (target, x, y, deviceType) => new MouseMoveCommand(target, x, y, deviceType),
            targetOption,
            catalog,
            executeCommandAsync);
        Command mouseClickCommand = CreatePointerCommand(
            "click",
            "Moves the mouse cursor and performs a primary click within the target window.",
            static (target, x, y, deviceType) => new MouseClickCommand(target, x, y, deviceType),
            targetOption,
            catalog,
            executeCommandAsync);

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
        typeCommand.Add(deviceOption);
        typeCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new TypeCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(typeTextArgument),
                    ParseKeyboardSequence(parseResult.GetRequiredValue(typeTextArgument)),
                    ParseOptionalDeviceType(parseResult.GetValue(deviceOption))),
                cancellationToken));

        Command inputDeviceAttachCommand = new("attach", "Attaches an input device to the target.");
        inputDeviceAttachCommand.Add(inputDeviceArgument);
        inputDeviceAttachCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new InputDeviceAttachCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    ParseRequiredDeviceType(parseResult.GetRequiredValue(inputDeviceArgument))),
                cancellationToken));

        Command inputDeviceRemoveCommand = new("remove", "Removes an input device from the target.");
        inputDeviceRemoveCommand.Add(inputDeviceArgument);
        inputDeviceRemoveCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new InputDeviceRemoveCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    ParseRequiredDeviceType(parseResult.GetRequiredValue(inputDeviceArgument))),
                cancellationToken));

        Command inputDeviceListCommand = new("list", "Lists the target's supported input devices and attachment state.");
        inputDeviceListCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new InputDeviceListCommand(ResolveTarget(parseResult, targetOption, catalog)),
                cancellationToken));

        Command inputDeviceCommand = new("inputdevice", "Manages attached input devices for the target.");
        inputDeviceCommand.Add(inputDeviceAttachCommand);
        inputDeviceCommand.Add(inputDeviceRemoveCommand);
        inputDeviceCommand.Add(inputDeviceListCommand);

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
        Option<CropRectangle?> recordCropOption = new("--crop")
        {
            Description = "Captures only the specified rectangle as x,y,width,height.",
            HelpName = "x,y,width,height",
        };
        recordCropOption.CustomParser = ParseRecordingCrop;
        Command recordStartCommand = new("start", "Starts recording the target window.");
        recordStartCommand.Add(recordOutputOption);
        recordStartCommand.Add(recordTimeLimitOption);
        recordStartCommand.Add(excludeCursorOption);
        recordStartCommand.Add(recordCropOption);
        recordStartCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new RecordStartCommand(
                    ResolveTarget(parseResult, targetOption, catalog),
                    parseResult.GetRequiredValue(recordOutputOption),
                    parseResult.GetValue(recordTimeLimitOption),
                    parseResult.GetValue(excludeCursorOption),
                    parseResult.GetValue(recordCropOption)),
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

        Command recordStatusCommand = new("status", "Reports the current or most recent recording state.");
        recordStatusCommand.SetAction((parseResult, cancellationToken) =>
            executeCommandAsync(
                new RecordStatusCommand(ResolveTarget(parseResult, targetOption, catalog)),
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
            "Starts, stops, or cancels recording the target window.");
        recordCommand.Add(recordStartCommand);
        recordCommand.Add(recordStopCommand);
        recordCommand.Add(recordCancelCommand);
        recordCommand.Add(recordCaptionCommand);
        recordCommand.Add(recordStatusCommand);

        rootCommand.SetAction(parseResult => new HelpAction().Invoke(parseResult));
        rootCommand.Add(targetOption);
        rootCommand.Add(targetCommand);
        rootCommand.Add(tapCommand);
        rootCommand.Add(mouseMoveCommand);
        rootCommand.Add(mouseClickCommand);
        rootCommand.Add(typeCommand);
        rootCommand.Add(inputDeviceCommand);
        rootCommand.Add(resizeCommand);
        rootCommand.Add(screenshotCommand);
        rootCommand.Add(recordCommand);
        return rootCommand;
    }

    private static Command CreatePointerCommand(
        string name,
        string description,
        Func<TargetApplication?, int, int, InputDeviceType?, AppCapCommand> commandFactory,
        Option<string?> targetOption,
        TargetCatalog? catalog,
        Func<AppCapCommand, CancellationToken, Task> executeCommandAsync)
    {
        Option<int?> xOption = OptionalIntegerOption("-x", "pixels", "Sets the horizontal coordinate in pixels within the target window.", value => value >= 0);
        Option<int?> yOption = OptionalIntegerOption("-y", "pixels", "Sets the vertical coordinate in pixels within the target window.", value => value >= 0);
        Argument<CoordinatePair?> coordinates = new("coordinates")
        {
            Description = "Specifies the coordinates as x,y.",
            HelpName = "x,y",
        };
        coordinates.CustomParser = ParseCoordinates;
        Option<string?> device = CreateDeviceOption();

        Command command = new(name, description);
        command.Add(coordinates);
        command.Add(xOption);
        command.Add(yOption);
        command.Add(device);
        command.Validators.Add(result =>
        {
            bool hasCoordinates = result.GetResult(coordinates)?.Tokens.Count > 0;
            bool hasX = result.GetResult(xOption)?.Tokens.Count > 0;
            bool hasY = result.GetResult(yOption)?.Tokens.Count > 0;
            if (hasCoordinates && (hasX || hasY))
            {
                result.AddError("Specify coordinates either as x,y or with -x and -y, not both.");
            }
            else if (!hasCoordinates && (!hasX || !hasY))
            {
                result.AddError("Coordinates are required. Specify x,y or both -x and -y.");
            }
        });
        command.SetAction((parseResult, cancellationToken) =>
        {
            CoordinatePair? pair = parseResult.GetValue(coordinates);
            return executeCommandAsync(
                commandFactory(
                    ResolveTarget(parseResult, targetOption, catalog),
                    pair?.X ?? parseResult.GetValue(xOption) ?? throw MissingCoordinatesException(),
                    pair?.Y ?? parseResult.GetValue(yOption) ?? throw MissingCoordinatesException(),
                    ParseOptionalDeviceType(parseResult.GetValue(device))),
                cancellationToken);
        });
        return command;
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

    private static Option<int?> OptionalIntegerOption(
        string name,
        string helpName,
        string description,
        Func<int, bool> isValid)
    {
        Option<int?> option = new(name)
        {
            Description = description,
            HelpName = helpName,
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

    private static Option<string?> CreateDeviceOption()
    {
        Option<string?> option = new("--device")
        {
            Description = "Selects an attached input device by type.",
            HelpName = "device",
        };
        option.CompletionSources.Add(_ => InputDeviceType.KnownTypes.Select(deviceType => deviceType.ToString()));
        option.Validators.Add(ValidateInputDeviceToken);
        return option;
    }

    private static Argument<string> CreateInputDeviceArgument()
    {
        Argument<string> argument = new("device")
        {
            Description = "Specifies the input device type.",
            HelpName = "device",
        };
        argument.CompletionSources.Add(_ => InputDeviceType.KnownTypes.Select(deviceType => deviceType.ToString()));
        argument.Validators.Add(ValidateInputDeviceToken);
        return argument;
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

    private static InputDeviceType ParseRequiredDeviceType(string value)
    {
        if (!InputDeviceType.TryParse(value, out InputDeviceType deviceType))
        {
            throw new AppCapException("Invalid input device identifier.", ExitCodes.UsageError);
        }

        return deviceType;
    }

    private static InputDeviceType? ParseOptionalDeviceType(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseRequiredDeviceType(value);

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

    private static CoordinatePair? ParseCoordinates(ArgumentResult result)
    {
        if (result.Tokens.Count is 0)
        {
            return null;
        }

        string[] parts = result.Tokens.Count is 1
            ? result.Tokens[0].Value.Split(',', StringSplitOptions.TrimEntries)
            : [];
        if (parts.Length is not 2 ||
            !int.TryParse(parts[0], out int x) ||
            !int.TryParse(parts[1], out int y) ||
            x < 0 ||
            y < 0)
        {
            result.AddError("Invalid coordinates. Expected x,y with nonnegative integer values.");
            return null;
        }

        return new CoordinatePair(x, y);
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

    private static CropRectangle? ParseRecordingCrop(ArgumentResult result)
    {
        CropRectangle? crop = ParseCrop(result);
        if (crop is { } value && (value.Width % 2 != 0 || value.Height % 2 != 0))
        {
            result.AddError("Invalid value for --crop. Width and height must be even.");
            return null;
        }

        return crop;
    }

    private static TargetApplication? ResolveTarget(
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
            : null;
    }

    private static TargetApplication? ResolveOptionalTarget(string? value, TargetCatalog? catalog)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (catalog is not null && catalog.TryParse(value, out TargetApplication target))
        {
            return target;
        }

        throw new AppCapException($"Unknown target '{value}'.", ExitCodes.UsageError);
    }

    private static void ValidateTargetName(ArgumentResult result, TargetCatalog catalog)
    {
        string? value = result.GetValueOrDefault<string?>();
        if (value is not null && !catalog.TryParse(value, out _))
        {
            result.AddError($"Unknown target '{value}'.");
        }
    }

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static AppCapException MissingCoordinatesException() =>
        new("Coordinates are required. Specify x,y or both -x and -y.", ExitCodes.UsageError);

    private static void ValidateInputDeviceToken(SymbolResult result)
    {
        if (result.Tokens.Count is 0)
        {
            return;
        }

        string? value = result.Tokens[0].Value;
        if (!InputDeviceType.TryParse(value, out _))
        {
            result.AddError("Invalid input device identifier.");
        }
    }

    private readonly record struct CoordinatePair(int X, int Y);
}