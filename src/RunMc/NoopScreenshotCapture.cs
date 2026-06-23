namespace RunMc;

public sealed class NoopScreenshotCapture : IScreenshotCapture
{
    public Task CapturePngAsync(MinecraftWindow window, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        throw new RunMcException("Screenshot capture is not implemented yet.");
    }
}