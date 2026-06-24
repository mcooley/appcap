namespace RunMc;

public static class WindowsCommandServices
{
    public static IPhaseOneCommandRunner CreatePhaseOneRunner()
    {
        WindowsWindowController windowController = new();
        return new PhaseOneCommandRunner(
            new WindowsBedrockTargetResolver(new WindowsMinecraftWindowFinder(), new WindowsAppLauncher()),
            windowController,
            new SyntheticPointerInputInjector(),
            new WindowsGraphicsCaptureScreenshotCapture());
    }
}