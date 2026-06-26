namespace AppCap.E2ETests;

public sealed class ClickCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public async Task ClickChangesObservablePixel()
    {
        Context.Run("resize", "--width", "640", "--height", "480").AssertSuccess();
        Context.Run("click", "-x", "150", "-y", "130").AssertSuccess();

        string path = Context.NewOutputPath("click-state.png");
        Context.Run("screenshot", "--output", path).AssertSuccess();

        PixelColor clickPixel = await E2EHelpers.ReadPixelAsync(path, 150, 130);
        PixelAssertions.AssertColorNear(new PixelColor(220, 40, 40), clickPixel);
    }
}