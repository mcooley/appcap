namespace RunMc.Tests;

public sealed class BedrockPackageTests
{
    [Fact]
    public void RetailAumidUsesGameEntryPoint()
    {
        Assert.Equal("Microsoft.MinecraftUWP_8wekyb3d8bbwe!Game", BedrockPackage.RetailAumid);
    }

    [Fact]
    public void PreviewAumidUsesGameEntryPoint()
    {
        Assert.Equal("Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe!Game", BedrockPackage.PreviewAumid);
    }
}