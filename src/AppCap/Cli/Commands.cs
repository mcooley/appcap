namespace AppCap;

public abstract record AppCapCommand(TargetConfiguration Target);

public sealed record FocusCommand(TargetConfiguration Target) : AppCapCommand(Target);

public sealed record ClickCommand(TargetConfiguration Target, int X, int Y) : AppCapCommand(Target);

public sealed record HoverCommand(TargetConfiguration Target, int X, int Y) : AppCapCommand(Target);

public sealed record TypeCommand(TargetConfiguration Target, IReadOnlyList<KeyboardAction> Actions) : AppCapCommand(Target);

public sealed record ResizeCommand(TargetConfiguration Target, int Width, int Height) : AppCapCommand(Target);

public sealed record ScreenshotCommand(TargetConfiguration Target, string OutputPath, bool IncludeCursor, string? Caption) : AppCapCommand(Target);

public sealed record RecordStartCommand(TargetConfiguration Target, string OutputPath) : AppCapCommand(Target);

public sealed record RecordStopCommand(TargetConfiguration Target) : AppCapCommand(Target);

public sealed record RecordCancelCommand(TargetConfiguration Target) : AppCapCommand(Target);

public sealed record HelpCommand(HelpTopic Topic) : AppCapCommand(TargetParser.Default);

public sealed record ParseResult(bool Success, AppCapCommand Command, string? ErrorMessage)
{
    public static ParseResult Valid(AppCapCommand command) => new(true, command, null);

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

