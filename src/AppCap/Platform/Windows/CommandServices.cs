using AppCap;
namespace AppCap.Windows;

public static class CommandServices
{
    public static ICommandRunner CreateRunner()
    {
        WindowController windowController = new();
        return new CommandRunner(
            new TargetResolver(new WindowFinder(), new TargetLauncher()),
            windowController,
            new WorkerInputController(),
            new WorkerScreenshotClient(),
            new RecordingController());
    }
}