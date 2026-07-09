namespace AppCap;

public interface IRecordingController
{
    Task StartAsync(TargetWindow window, string outputPath, TimeSpan timeLimit, bool includeCursor, CancellationToken cancellationToken);

    Task AddCaptionAsync(TargetConfiguration target, string caption, CancellationToken cancellationToken);

    Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken);

    Task CancelAsync(TargetConfiguration target, CancellationToken cancellationToken);
}