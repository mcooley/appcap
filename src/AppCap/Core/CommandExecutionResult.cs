namespace AppCap;

public sealed record CommandExecutionResult(string Output)
{
    public static CommandExecutionResult Empty { get; } = new(string.Empty);
}