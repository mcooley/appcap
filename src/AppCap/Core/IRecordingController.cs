namespace AppCap;

public interface IRecordingController
{
    Task StartAsync(TargetWindow window, string outputPath, CancellationToken cancellationToken);

    Task StopAsync(TargetApplication target, CancellationToken cancellationToken);

    Task CancelAsync(TargetApplication target, CancellationToken cancellationToken);
}