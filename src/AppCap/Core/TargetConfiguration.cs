namespace AppCap;

public sealed record TargetConfiguration(string Name, IReadOnlyList<TargetApplication> Applications);

public sealed record TargetApplication(string Name, string PackageFamilyName, string Aumid);