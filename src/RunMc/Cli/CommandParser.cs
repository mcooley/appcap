namespace RunMc;

public static class CommandParser
{
    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        TargetKind target = TargetKind.Default;
        List<string> remainingArgs = [];

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (arg is "--target")
            {
                if (++index >= args.Count)
                {
                    return ParseResult.Failure("Missing value for --target.");
                }

                if (!TargetKindParser.TryParse(args[index], out target))
                {
                    return ParseResult.Failure($"Unknown target '{args[index]}'.");
                }

                continue;
            }

            remainingArgs.Add(arg);
        }

        if (remainingArgs.Count is 0)
        {
            return ParseResult.Valid(new FocusCommand(target));
        }

        string commandName = remainingArgs[0];
        if (commandName is "--help" or "-h" or "help")
        {
            return ParseResult.Valid(new HelpCommand(HelpTopic.Root));
        }

        IReadOnlyList<string> commandArgs = remainingArgs.Skip(1).ToArray();
        if (commandArgs.Contains("--help", StringComparer.Ordinal) || commandArgs.Contains("-h", StringComparer.Ordinal))
        {
            return ParseResult.Valid(new HelpCommand(ParseHelpTopic(commandName)));
        }

        return commandName switch
        {
            "click" => ParseClick(target, commandArgs),
            "resize" => ParseResize(target, commandArgs),
            "screenshot" => ParseScreenshot(target, commandArgs),
            _ => ParseResult.Failure($"Unknown command '{commandName}'."),
        };
    }

    private static ParseResult ParseClick(TargetKind target, IReadOnlyList<string> args)
    {
        OptionReader reader = new(args);
        int? x = null;
        int? y = null;

        while (reader.TryReadOption(out string? name, out string? value))
        {
            if (name is "-x")
            {
                if (!TryParseNonNegativeInt(value, out int parsedX))
                {
                    return ParseResult.Failure("Invalid value for -x.");
                }

                x = parsedX;
            }
            else if (name is "-y")
            {
                if (!TryParseNonNegativeInt(value, out int parsedY))
                {
                    return ParseResult.Failure("Invalid value for -y.");
                }

                y = parsedY;
            }
            else
            {
                return ParseResult.Failure($"Unknown option '{name}'.");
            }
        }

        return x.HasValue && y.HasValue
            ? ParseResult.Valid(new ClickCommand(target, x.Value, y.Value))
            : ParseResult.Failure("click requires -x and -y.");
    }

    private static ParseResult ParseResize(TargetKind target, IReadOnlyList<string> args)
    {
        OptionReader reader = new(args);
        int? width = null;
        int? height = null;

        while (reader.TryReadOption(out string? name, out string? value))
        {
            if (name is "--width")
            {
                if (!TryParsePositiveInt(value, out int parsedWidth))
                {
                    return ParseResult.Failure("Invalid value for --width.");
                }

                width = parsedWidth;
            }
            else if (name is "--height")
            {
                if (!TryParsePositiveInt(value, out int parsedHeight))
                {
                    return ParseResult.Failure("Invalid value for --height.");
                }

                height = parsedHeight;
            }
            else
            {
                return ParseResult.Failure($"Unknown option '{name}'.");
            }
        }

        return width.HasValue && height.HasValue
            ? ParseResult.Valid(new ResizeCommand(target, width.Value, height.Value))
            : ParseResult.Failure("resize requires --width and --height.");
    }

    private static ParseResult ParseScreenshot(TargetKind target, IReadOnlyList<string> args)
    {
        OptionReader reader = new(args);
        string? outputPath = null;

        while (reader.TryReadOption(out string? name, out string? value))
        {
            if (name is "--output")
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return ParseResult.Failure("Invalid value for --output.");
                }

                outputPath = value;
            }
            else
            {
                return ParseResult.Failure($"Unknown option '{name}'.");
            }
        }

        if (outputPath is null)
        {
            return ParseResult.Failure("screenshot requires --output.");
        }

        return Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? ParseResult.Valid(new ScreenshotCommand(target, outputPath))
            : ParseResult.Failure("screenshot output must be a .png file.");
    }

    private static HelpTopic ParseHelpTopic(string commandName) => commandName switch
    {
        "click" => HelpTopic.Click,
        "resize" => HelpTopic.Resize,
        "screenshot" => HelpTopic.Screenshot,
        _ => HelpTopic.Root,
    };

    private static bool TryParseNonNegativeInt(string? value, out int result) =>
        int.TryParse(value, out result) && result >= 0;

    private static bool TryParsePositiveInt(string? value, out int result) =>
        int.TryParse(value, out result) && result > 0;
}