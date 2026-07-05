namespace AppCap;

public interface IRecordingController
{
    Task StartAsync(TargetWindow window, string outputPath, TimeSpan timeLimit, bool includeCursor, CancellationToken cancellationToken);

    Task AddCaptionAsync(TargetApplication target, string caption, CancellationToken cancellationToken);

    Task StopAsync(TargetApplication target, CancellationToken cancellationToken);

    Task CancelAsync(TargetApplication target, CancellationToken cancellationToken);
}