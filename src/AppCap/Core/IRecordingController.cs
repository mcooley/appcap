namespace AppCap;

public interface IRecordingController
{
    Task StartAsync(TargetWindow window, string outputPath, TimeSpan timeLimit, bool includeCursor, CancellationToken cancellationToken);

    Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken);

    Task CancelAsync(TargetConfiguration target, CancellationToken cancellationToken);
}