using AppCap;
namespace AppCap.Windows;

public static class CommandServices
{
    public static ICommandRunner CreateRunner(TargetCatalog catalog)
    {
        WindowFinder windowFinder = new();
        TargetResolver targetResolver = new(windowFinder, new TargetLauncher());
        WindowController windowController = new();
        return new CommandRunner(
            targetResolver,
            windowController,
            new WorkerInputController(),
            new WorkerScreenshotClient(),
            new RecordingController(),
            new TargetSessionController(catalog, windowFinder, targetResolver));
    }
}