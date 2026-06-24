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

    private static readonly TargetApplication TestApp = new(
        "testapp",
        "RunMc.E2ETestApp_87ehf5vpf4evy",
        "RunMc.E2ETestApp_87ehf5vpf4evy!App");

    public static TargetConfiguration Default { get; } = new("default", [Bedrock, BedrockPreview, Education]);

    public static bool TryParse(string value, out TargetConfiguration target)
    {
        target = value switch
        {
            "bedrock" => new TargetConfiguration("bedrock", [Bedrock]),
            "bedrockpreview" => new TargetConfiguration("bedrockpreview", [BedrockPreview]),
            "education" => new TargetConfiguration("education", [Education]),
            "testapp" => new TargetConfiguration("testapp", [TestApp]),
            _ => Default,
        };

        return target != Default;
    }
}