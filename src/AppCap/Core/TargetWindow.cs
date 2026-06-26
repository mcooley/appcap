namespace AppCap;

public sealed record TargetWindow(TargetConfiguration Target, TargetApplication Application, nint Handle);