using RunMc;
namespace RunMc.Windows;

public static class CommandServices
{
    public static IPhaseOneCommandRunner CreatePhaseOneRunner()
    {
        WindowController windowController = new();
        return new PhaseOneCommandRunner(
            new BedrockTargetResolver(new MinecraftWindowFinder(), new AppLauncher()),
            windowController,
            new SyntheticPointerInputInjector(),
            new CursorMover(),
            new KeyboardInputInjector(),
            new GraphicsCaptureScreenshotCapture());
    }
}