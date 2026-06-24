namespace RunMc;

public static class TargetParser
{
    private static readonly TargetApplication Bedrock = new(
        "bedrock",
        "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
        "Microsoft.MinecraftUWP_8wekyb3d8bbwe!Game");

    private static readonly TargetApplication BedrockPreview = new(
        "bedrockpreview",
        "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe",
        "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe!Game");

    private static readonly TargetApplication Education = new(
        "education",
        "Microsoft.MinecraftEducationEdition_8wekyb3d8bbwe",
        "Microsoft.MinecraftEducationEdition_8wekyb3d8bbwe!Microsoft.MinecraftEducationEdition");

    public static TargetConfiguration Default { get; } = new("default", [Bedrock, BedrockPreview, Education]);

    public static bool TryParse(string value, out TargetConfiguration target)
    {
        target = value switch
        {
            "bedrock" => new TargetConfiguration("bedrock", [Bedrock]),
            "bedrockpreview" => new TargetConfiguration("bedrockpreview", [BedrockPreview]),
            "education" => new TargetConfiguration("education", [Education]),
            _ => Default,
        };

        return target != Default;
    }
}