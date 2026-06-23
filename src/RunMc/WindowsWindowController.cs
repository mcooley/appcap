namespace RunMc;

public sealed class WindowsWindowController : IWindowController
{
    public Task BringToForegroundAsync(MinecraftWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        _ = WindowsNative.ShowWindow(window.Handle, WindowsNative.SwRestore);
        if (!WindowsNative.SetForegroundWindow(window.Handle))
        {
            throw new RunMcException("Minecraft Bedrock window could not be focused.");
        }

        return Task.CompletedTask;
    }

    public Task<WindowBounds> GetBoundsAsync(MinecraftWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GetBounds(window));
    }

    public Task ResizeAsync(MinecraftWindow window, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        _ = WindowsNative.ShowWindow(window.Handle, WindowsNative.SwRestore);
        bool moved = WindowsNative.SetWindowPos(
            window.Handle,
            0,
            0,
            0,
            width,
            height,
            WindowsNative.SwpNoMove | WindowsNative.SwpNoZOrder | WindowsNative.SwpNoActivate);

        if (!moved)
        {
            throw new RunMcException("Requested window size is not possible.");
        }

        WindowBounds bounds = GetBounds(window);
        if (bounds.Width != width || bounds.Height != height)
        {
            throw new RunMcException("Requested window size is not possible.");
        }

        return Task.CompletedTask;
    }

    private static WindowBounds GetBounds(MinecraftWindow window)
    {
        if (!WindowsNative.GetWindowRect(window.Handle, out NativeRect rect))
        {
            throw new RunMcException("Minecraft Bedrock window bounds could not be read.");
        }

        return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
}