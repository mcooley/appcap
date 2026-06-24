namespace RunMc;

public abstract record RunMcCommand(TargetConfiguration Target);

public sealed record FocusCommand(TargetConfiguration Target) : RunMcCommand(Target);

public sealed record ClickCommand(TargetConfiguration Target, int X, int Y) : RunMcCommand(Target);

public sealed record HoverCommand(TargetConfiguration Target, int X, int Y) : RunMcCommand(Target);

public sealed record TypeCommand(TargetConfiguration Target, IReadOnlyList<KeyboardAction> Actions) : RunMcCommand(Target);

public sealed record ResizeCommand(TargetConfiguration Target, int Width, int Height) : RunMcCommand(Target);

public sealed record ScreenshotCommand(TargetConfiguration Target, string OutputPath, bool IncludeCursor, string? Caption) : RunMcCommand(Target);

public sealed record RecordStartCommand(TargetConfiguration Target, string OutputPath) : RunMcCommand(Target);

public sealed record RecordStopCommand(TargetConfiguration Target) : RunMcCommand(Target);

public sealed record RecordCancelCommand(TargetConfiguration Target) : RunMcCommand(Target);

public sealed record HelpCommand(HelpTopic Topic) : RunMcCommand(TargetParser.Default);

public sealed record ParseResult(bool Success, RunMcCommand Command, string? ErrorMessage)
{
    public static ParseResult Valid(RunMcCommand command) => new(true, command, null);

    public static ParseResult Failure(string errorMessage) => new(false, new HelpCommand(HelpTopic.Root), errorMessage);
}

public enum HelpTopic
{
    Root,
    Click,
    Hover,
    Type,
    Resize,
    Screenshot,
    Record,
}

