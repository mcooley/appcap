namespace RunMc;

public static class BedrockPackage
{
    public const string RetailFamilyName = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";

    public const string RetailAumid = RetailFamilyName + "!Game";

    public const string PreviewFamilyName = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";

    public const string PreviewAumid = PreviewFamilyName + "!Game";

    public static string FamilyNameFor(TargetKind target) => target switch
    {
        TargetKind.RunningBedrockPreview or TargetKind.InstalledBedrockPreview => PreviewFamilyName,
        _ => RetailFamilyName,
    };

    public static string AumidFor(TargetKind target) => target switch
    {
        TargetKind.RunningBedrockPreview or TargetKind.InstalledBedrockPreview => PreviewAumid,
        _ => RetailAumid,
    };
}