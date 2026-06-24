namespace RunMc;

public interface IScreenshotCapture
{
    Task CapturePngAsync(MinecraftWindow window, string outputPath, bool includeCursor, CancellationToken cancellationToken);
}