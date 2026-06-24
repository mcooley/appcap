namespace RunMc;

public static class BedrockPackage
{
    public const string RetailFamilyName = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";

    public const string RetailAumid = RetailFamilyName + "!Game";

    public const string PreviewFamilyName = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";

    public const string PreviewAumid = PreviewFamilyName + "!Game";

    public const string EducationFamilyName = "Microsoft.MinecraftEducationEdition_8wekyb3d8bbwe";

    public const string EducationAumid = EducationFamilyName + "!Microsoft.MinecraftEducationEdition";

    public static string FamilyNameFor(TargetKind target) => target switch
    {
        TargetKind.RunningBedrockPreview or TargetKind.InstalledBedrockPreview => PreviewFamilyName,
        TargetKind.RunningEducation or TargetKind.InstalledEducation => EducationFamilyName,
        _ => RetailFamilyName,
    };

    public static string AumidFor(TargetKind target) => target switch
    {
        TargetKind.RunningBedrockPreview or TargetKind.InstalledBedrockPreview => PreviewAumid,
        TargetKind.RunningEducation or TargetKind.InstalledEducation => EducationAumid,
        _ => RetailAumid,
    };
}