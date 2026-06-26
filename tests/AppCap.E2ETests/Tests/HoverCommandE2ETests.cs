namespace AppCap.E2ETests;

public sealed class HoverCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public async Task HoverChangesObservablePixel()
    {
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("hover", "-x", "330", "-y", "130").AssertSuccess();

        string path = Context.NewOutputPath("hover-state.png");
        Context.Run("screenshot", "--output", path).AssertSuccess();

        PixelColor hoverPixel = await E2EHelpers.ReadPixelAsync(path, 330, 130);
        PixelAssertions.AssertColorNear(new PixelColor(245, 210, 40), hoverPixel);
    }
}