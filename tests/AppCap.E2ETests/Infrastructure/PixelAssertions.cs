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
}