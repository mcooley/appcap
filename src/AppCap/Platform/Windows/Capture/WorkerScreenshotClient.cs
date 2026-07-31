using AppCap;
using AppCap.Protocol.Worker;

namespace AppCap.Windows;

// Client-side IScreenshotCapture that asks the attached machine worker to capture and save
// a screenshot. The worker reuses a recording frame when available and otherwise captures
// through the attached target session.
public sealed class WorkerScreenshotClient : IScreenshotCapture
{
    public async Task CapturePngAsync(TargetWindow window, string outputPath, bool includeCursor, string? caption, CropRectangle? crop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        // Send an absolute path: a recording worker may run with a different working
        // directory than this client, and it is the worker that writes the file.
        ScreenshotRequest request = new()
        {
            TargetName = window.Application.Name,
            OutputPath = Path.GetFullPath(outputPath),
            IncludeCursor = includeCursor,
            Caption = caption,
            Crop = crop,
        };

        if (!await RecordingIpc.CaptureScreenshotAsync(window.Application.Name, request, cancellationToken).ConfigureAwait(false))
        {
            throw new AppCapException($"Target '{window.Application.Name}' is not attached.", ExitCodes.UsageError);
        }
    }
}
