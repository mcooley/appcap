using RunMc;
namespace RunMc.Windows;

public static class CommandServices
{
    public static ICommandRunner CreateRunner()
    {
        WindowController windowController = new();
        return new CommandRunner(
            new BedrockTargetResolver(new MinecraftWindowFinder(), new AppLauncher()),
            windowController,
            new SyntheticPointerInputInjector(),
            new CursorMover(),
            new KeyboardInputInjector(),
            new GraphicsCaptureScreenshotCapture());
    }
}