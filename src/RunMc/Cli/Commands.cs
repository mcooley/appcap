namespace RunMc;

public abstract record RunMcCommand(TargetKind Target);

public sealed record FocusCommand(TargetKind Target) : RunMcCommand(Target);

public sealed record ClickCommand(TargetKind Target, int X, int Y) : RunMcCommand(Target);

public sealed record HoverCommand(TargetKind Target, int X, int Y) : RunMcCommand(Target);

public sealed record TypeCommand(TargetKind Target, IReadOnlyList<KeyboardAction> Actions) : RunMcCommand(Target);

public sealed record ResizeCommand(TargetKind Target, int Width, int Height) : RunMcCommand(Target);

public sealed record ScreenshotCommand(TargetKind Target, string OutputPath, bool IncludeCursor, string? Caption) : RunMcCommand(Target);

public sealed record HelpCommand(HelpTopic Topic) : RunMcCommand(TargetKind.Default);

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
}

public enum TargetKind
{
    Default,
    RunningBedrock,
    RunningBedrockPreview,
    RunningEducation,
    RunningJava,
    InstalledBedrock,
    InstalledBedrockPreview,
    InstalledEducation,
    InstalledJava,
}