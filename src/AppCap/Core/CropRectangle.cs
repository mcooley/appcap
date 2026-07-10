using System.Globalization;
using System.Text.Json.Serialization;

namespace AppCap;

public readonly record struct CropRectangle(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height)
{
    public static bool TryParse(string? value, out CropRectangle crop)
    {
        crop = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not 4 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
            x < 0 ||
            y < 0 ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        crop = new CropRectangle(x, y, width, height);
        return true;
    }

    public void ValidateWithin(int sourceWidth, int sourceHeight, string captureDescription = "captured frame")
    {
        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0)
        {
            throw new AppCapException(
                $"Crop '{ToInvariantString()}' is invalid. Use x,y,width,height with nonnegative x/y and positive width/height.",
                ExitCodes.UsageError);
        }

        if ((long)X + Width > sourceWidth || (long)Y + Height > sourceHeight)
        {
            throw new AppCapException(
                $"Crop '{ToInvariantString()}' exceeds the {captureDescription} size {sourceWidth}x{sourceHeight}. " +
                $"Use x,y,width,height with x + width <= {sourceWidth} and y + height <= {sourceHeight}.");
        }
    }

    public string ToInvariantString() =>
        string.Join(
            ",",
            X.ToString(CultureInfo.InvariantCulture),
            Y.ToString(CultureInfo.InvariantCulture),
            Width.ToString(CultureInfo.InvariantCulture),
            Height.ToString(CultureInfo.InvariantCulture));
}
