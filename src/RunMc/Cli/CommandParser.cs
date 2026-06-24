using System.CommandLine;
using System.CommandLine.Parsing;

namespace RunMc;

public static class CommandParser
{
    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count is 0)
        {
            return ParseResult.Valid(new FocusCommand(TargetParser.Default));
        }

        if (TryParseHelp(args, out HelpTopic helpTopic))
        {
            return ParseResult.Valid(new HelpCommand(helpTopic));
        }

        CommandLineModel model = CommandLineModel.Create();
        System.CommandLine.ParseResult result = model.RootCommand.Parse(args);
        if (result.Errors.Count > 0)
        {
            return ParseResult.Failure(result.Errors[0].Message);
        }

        string? targetValue = result.GetValue(model.TargetOption);
        TargetConfiguration target = TargetParser.Default;
        if (targetValue is not null && !TargetParser.TryParse(targetValue, out target))
        {
            return ParseResult.Failure($"Unknown target '{targetValue}'.");
        }

        Command command = result.CommandResult.Command;
        if (command == model.RootCommand)
        {
            return ParseResult.Valid(new FocusCommand(target));
        }

        if (command == model.ClickCommand)
        {
            return ParseResult.Valid(new ClickCommand(
                target,
                result.GetRequiredValue(model.ClickXOption),
                result.GetRequiredValue(model.ClickYOption)));
        }

        if (command == model.HoverCommand)
        {
            return ParseResult.Valid(new HoverCommand(
                target,
                result.GetRequiredValue(model.HoverXOption),
                result.GetRequiredValue(model.HoverYOption)));
        }

        if (command == model.TypeCommand)
        {
            string sequence = result.GetRequiredValue(model.TypeTextArgument);
            if (!KeyboardSequenceParser.TryParse(sequence, out IReadOnlyList<KeyboardAction> actions, out string? errorMessage))
            {
                return ParseResult.Failure(errorMessage ?? "Invalid keyboard sequence.");
            }

            return ParseResult.Valid(new TypeCommand(target, actions));
        }

        if (command == model.ResizeCommand)
        {
            return ParseResult.Valid(new ResizeCommand(
                target,
                result.GetRequiredValue(model.ResizeWidthOption),
                result.GetRequiredValue(model.ResizeHeightOption)));
        }

        if (command == model.ScreenshotCommand)
        {
            return ParseResult.Valid(new ScreenshotCommand(
                target,
                result.GetRequiredValue(model.ScreenshotOutputOption),
                result.GetValue(model.ScreenshotIncludeCursorOption),
                NormalizeOptionalText(result.GetValue(model.ScreenshotCaptionOption))));
        }

        return ParseResult.Failure($"Unknown command '{command.Name}'.");
    }

    private static bool TryParseHelp(IReadOnlyList<string> args, out HelpTopic topic)
    {
        topic = HelpTopic.Root;
        if (args.Count is 0)
        {
            return false;
        }

        if (args[0] is "--help" or "help")
        {
            return true;
        }

        if (!args.Contains("--help", StringComparer.Ordinal))
        {
            return false;
        }

        topic = args[0] switch
        {
            "click" => HelpTopic.Click,
            "hover" => HelpTopic.Hover,
            "type" => HelpTopic.Type,
            "resize" => HelpTopic.Resize,
            "screenshot" => HelpTopic.Screenshot,
            _ => HelpTopic.Root,
        };
        return true;
    }

    private static Option<int> RequiredNonNegativeIntegerOption(string name, string errorName, params string[] aliases)
    {
        Option<int> option = new(name, aliases)
        {
            Required = true,
        };
        option.CustomParser = result => ParseInteger(result, errorName, value => value >= 0);
        return option;
    }

    private static Option<int> RequiredPositiveIntegerOption(string name, string errorName, params string[] aliases)
    {
        Option<int> option = new(name, aliases)
        {
            Required = true,
        };
        option.CustomParser = result => ParseInteger(result, errorName, value => value > 0);
        return option;
    }

    private static int ParseInteger(ArgumentResult result, string errorName, Func<int, bool> isValid)
    {
        string? value = result.Tokens.Count is 1 ? result.Tokens[0].Value : null;
        if (!int.TryParse(value, out int parsed) || !isValid(parsed))
        {
            result.AddError($"Invalid value for {errorName}.");
            return 0;
        }

        return parsed;
    }

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class CommandLineModel
    {
        private CommandLineModel(
            RootCommand rootCommand,
            Option<string?> targetOption,
            Command clickCommand,
            Option<int> clickXOption,
            Option<int> clickYOption,
            Command hoverCommand,
            Option<int> hoverXOption,
            Option<int> hoverYOption,
            Command typeCommand,
            Argument<string> typeTextArgument,
            Command resizeCommand,
            Option<int> resizeWidthOption,
            Option<int> resizeHeightOption,
            Command screenshotCommand,
            Option<string> screenshotOutputOption,
            Option<bool> screenshotIncludeCursorOption,
            Option<string?> screenshotCaptionOption)
        {
            RootCommand = rootCommand;
            TargetOption = targetOption;
            ClickCommand = clickCommand;
            ClickXOption = clickXOption;
            ClickYOption = clickYOption;
            HoverCommand = hoverCommand;
            HoverXOption = hoverXOption;
            HoverYOption = hoverYOption;
            TypeCommand = typeCommand;
            TypeTextArgument = typeTextArgument;
            ResizeCommand = resizeCommand;
            ResizeWidthOption = resizeWidthOption;
            ResizeHeightOption = resizeHeightOption;
            ScreenshotCommand = screenshotCommand;
            ScreenshotOutputOption = screenshotOutputOption;
            ScreenshotIncludeCursorOption = screenshotIncludeCursorOption;
            ScreenshotCaptionOption = screenshotCaptionOption;
        }

        public RootCommand RootCommand { get; }

        public Option<string?> TargetOption { get; }

        public Command ClickCommand { get; }

        public Option<int> ClickXOption { get; }

        public Option<int> ClickYOption { get; }

        public Command HoverCommand { get; }

        public Option<int> HoverXOption { get; }

        public Option<int> HoverYOption { get; }

        public Command TypeCommand { get; }

        public Argument<string> TypeTextArgument { get; }

        public Command ResizeCommand { get; }

        public Option<int> ResizeWidthOption { get; }

        public Option<int> ResizeHeightOption { get; }

        public Command ScreenshotCommand { get; }

        public Option<string> ScreenshotOutputOption { get; }

        public Option<bool> ScreenshotIncludeCursorOption { get; }

        public Option<string?> ScreenshotCaptionOption { get; }

        public static CommandLineModel Create()
        {
            Option<string?> targetOption = new("--target")
            {
                Recursive = true,
            };

            Option<int> clickXOption = RequiredNonNegativeIntegerOption("-x", "-x");
            Option<int> clickYOption = RequiredNonNegativeIntegerOption("-y", "-y");
            Command clickCommand = new("click", "Injects a mouse click into the target window.");
            clickCommand.Add(clickXOption);
            clickCommand.Add(clickYOption);

            Option<int> hoverXOption = RequiredNonNegativeIntegerOption("-x", "-x");
            Option<int> hoverYOption = RequiredNonNegativeIntegerOption("-y", "-y");
            Command hoverCommand = new("hover", "Moves the cursor over the target window.");
            hoverCommand.Add(hoverXOption);
            hoverCommand.Add(hoverYOption);

            Argument<string> typeTextArgument = new("text-and-keys");
            Command typeCommand = new("type", "Injects keyboard input into the target window.");
            typeCommand.Add(typeTextArgument);

            Option<int> resizeWidthOption = RequiredPositiveIntegerOption("--width", "--width", "-w");
            Option<int> resizeHeightOption = RequiredPositiveIntegerOption("--height", "--height", "-h");
            Command resizeCommand = new("resize", "Resizes the target window.");
            resizeCommand.Add(resizeWidthOption);
            resizeCommand.Add(resizeHeightOption);

            Option<string> screenshotOutputOption = new("--output")
            {
                Required = true,
            };
            screenshotOutputOption.CustomParser = result =>
            {
                string? value = result.Tokens.Count is 1 ? result.Tokens[0].Value : null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    result.AddError("Invalid value for --output.");
                    return string.Empty;
                }

                if (!Path.GetExtension(value).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError("screenshot output must be a .png file.");
                    return string.Empty;
                }

                return value;
            };
            Command screenshotCommand = new("screenshot", "Takes a PNG screenshot of the target window.");
            Option<bool> screenshotIncludeCursorOption = new("--include-cursor");
            Option<string?> screenshotCaptionOption = new("--caption");
            screenshotCommand.Add(screenshotOutputOption);
            screenshotCommand.Add(screenshotIncludeCursorOption);
            screenshotCommand.Add(screenshotCaptionOption);

            RootCommand rootCommand = new("Automates interactions with a configured target application.");
            rootCommand.Add(targetOption);
            rootCommand.Add(clickCommand);
            rootCommand.Add(hoverCommand);
            rootCommand.Add(typeCommand);
            rootCommand.Add(resizeCommand);
            rootCommand.Add(screenshotCommand);

            return new CommandLineModel(
                rootCommand,
                targetOption,
                clickCommand,
                clickXOption,
                clickYOption,
                hoverCommand,
                hoverXOption,
                hoverYOption,
                typeCommand,
                typeTextArgument,
                resizeCommand,
                resizeWidthOption,
                resizeHeightOption,
                screenshotCommand,
                screenshotOutputOption,
                screenshotIncludeCursorOption,
                screenshotCaptionOption);
        }
    }
}