namespace AppCap;

public sealed record TargetWindow(TargetConfiguration Target, AppCapTargetConfig Application, nint Handle);