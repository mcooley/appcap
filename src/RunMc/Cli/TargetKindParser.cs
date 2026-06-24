namespace RunMc;

public static class TargetKindParser
{
    public static bool TryParse(string value, out TargetKind target)
    {
        target = value switch
        {
            "runningbedrock" => TargetKind.RunningBedrock,
            "runningbedrockpreview" => TargetKind.RunningBedrockPreview,
            "runningeducation" => TargetKind.RunningEducation,
            "runningjava" => TargetKind.RunningJava,
            "installedbedrock" => TargetKind.InstalledBedrock,
            "installedbedrockpreview" => TargetKind.InstalledBedrockPreview,
            "installededucation" => TargetKind.InstalledEducation,
            "installedjava" => TargetKind.InstalledJava,
            _ => TargetKind.Default,
        };

        return target is not TargetKind.Default;
    }
}