namespace AppCap;

public abstract record AppCapCommand;

public sealed record ClickCommand(TargetApplication Target, int X, int Y) : AppCapCommand;

public sealed record HoverCommand(TargetApplication Target, int X, int Y) : AppCapCommand;

public sealed record TypeCommand(TargetApplication Target, IReadOnlyList<KeyboardAction> Actions) : AppCapCommand;

public sealed record ResizeCommand(TargetApplication Target, int Width, int Height) : AppCapCommand;

public sealed record ScreenshotCommand(TargetApplication Target, string OutputPath, bool ExcludeCursor, string? Caption) : AppCapCommand;

public sealed record RecordStartCommand(TargetApplication Target, string OutputPath, TimeSpan TimeLimit, bool ExcludeCursor) : AppCapCommand;

public sealed record RecordCaptionCommand(TargetApplication Target, string Caption) : AppCapCommand;

public sealed record RecordStopCommand(TargetApplication Target) : AppCapCommand;

public sealed record RecordCancelCommand(TargetApplication Target) : AppCapCommand;

public sealed record HelpCommand(HelpTopic Topic) : AppCapCommand;

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
