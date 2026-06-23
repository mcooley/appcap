namespace RunMc;

public interface IScreenshotCapture
{
    Task CapturePngAsync(MinecraftWindow window, string outputPath, CancellationToken cancellationToken);
}