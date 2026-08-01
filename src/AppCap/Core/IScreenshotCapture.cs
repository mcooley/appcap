namespace AppCap;

public interface IScreenshotCapture
{
    Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CropRectangle? crop, CancellationToken cancellationToken);
}