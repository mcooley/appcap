namespace AppCap;

public interface IRecordingController
{
    Task StartAsync(TargetWindow window, string outputPath, CancellationToken cancellationToken);

    Task StopAsync(TargetConfiguration target, CancellationToken cancellationToken);

    Task CancelAsync(TargetConfiguration target, CancellationToken cancellationToken);
}