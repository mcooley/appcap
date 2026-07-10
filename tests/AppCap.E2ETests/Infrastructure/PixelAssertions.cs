namespace AppCap.E2ETests;

internal static class PixelAssertions
{
    public static void AssertColorNear(PixelColor expected, PixelColor actual)
    {
        Assert.InRange(actual.Red, expected.Red - 12, expected.Red + 12);
        Assert.InRange(actual.Green, expected.Green - 12, expected.Green + 12);
        Assert.InRange(actual.Blue, expected.Blue - 12, expected.Blue + 12);
    }

    public static void AssertColorNotNear(PixelColor expected, PixelColor actual)
    {
        bool isNear = Math.Abs(actual.Red - expected.Red) <= 12 &&
            Math.Abs(actual.Green - expected.Green) <= 12 &&
            Math.Abs(actual.Blue - expected.Blue) <= 12;
        Assert.False(isNear, $"Expected color not to be near RGB({expected.Red}, {expected.Green}, {expected.Blue}), but was RGB({actual.Red}, {actual.Green}, {actual.Blue}).");
    }

    public static void AssertRegionNear(PixelColor expected, ImagePixels image, int x, int y, int width, int height)
    {
        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                AssertColorNear(expected, image.GetPixel(x + offsetX, y + offsetY));
            }
        }
    }

    public static void AssertRegionContainsColorNotNear(PixelColor expected, ImagePixels image, int x, int y, int width, int height)
    {
        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                PixelColor actual = image.GetPixel(x + offsetX, y + offsetY);
                bool isNear = Math.Abs(actual.Red - expected.Red) <= 12 &&
                    Math.Abs(actual.Green - expected.Green) <= 12 &&
                    Math.Abs(actual.Blue - expected.Blue) <= 12;
                if (!isNear)
                {
                    return;
                }
            }
        }

        Assert.Fail($"Expected region {x},{y},{width},{height} to contain a pixel not near RGB({expected.Red}, {expected.Green}, {expected.Blue}).");
    }
}