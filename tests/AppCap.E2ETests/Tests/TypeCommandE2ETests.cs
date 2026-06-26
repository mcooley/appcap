namespace AppCap.E2ETests;

public sealed class TypeCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public async Task TypeChangesObservablePixel()
    {
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("type", "abc").AssertSuccess();

        string path = Context.NewOutputPath("type-state.png");
        Context.Run("screenshot", "--output", path).AssertSuccess();

        PixelColor textPixel = await E2EHelpers.ReadPixelAsync(path, 150, 290);
        PixelAssertions.AssertColorNear(new PixelColor(40, 190, 90), textPixel);
    }
}