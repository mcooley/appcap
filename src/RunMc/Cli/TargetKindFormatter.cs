namespace RunMc;

public static class TargetKindFormatter
{
    public static string Format(TargetKind target) => target switch
    {
        TargetKind.Default => "default",
        TargetKind.RunningBedrock => "runningbedrock",
        TargetKind.RunningBedrockPreview => "runningbedrockpreview",
        TargetKind.RunningEducation => "runningeducation",
        TargetKind.RunningJava => "runningjava",
        TargetKind.InstalledBedrock => "installedbedrock",
        TargetKind.InstalledBedrockPreview => "installedbedrockpreview",
        TargetKind.InstalledEducation => "installededucation",
        TargetKind.InstalledJava => "installedjava",
        _ => target.ToString(),
    };
}