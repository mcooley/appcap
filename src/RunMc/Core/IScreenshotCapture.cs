namespace RunMc;

public interface IScreenshotCapture
{
    Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CancellationToken cancellationToken);
}