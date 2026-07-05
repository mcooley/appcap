namespace AppCap;

public sealed record TargetConfiguration(string Name, IReadOnlyList<AppCapTargetConfig> Applications)
{
    public static TargetConfiguration None { get; } = new("none", []);
}