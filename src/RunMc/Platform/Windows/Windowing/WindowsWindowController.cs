namespace RunMc;

public sealed class WindowsWindowController : IWindowController
{
    public Task BringToForegroundAsync(MinecraftWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        _ = WindowsNative.ShowWindow(window.Handle, WindowsNative.SwRestore);
        if (IsForegroundBedrockWindow(window))
        {
            return Task.CompletedTask;
        }

        _ = WindowsNative.SetForegroundWindow(window.Handle);
        if (IsForegroundBedrockWindow(window))
        {
            return Task.CompletedTask;
        }

        _ = WindowsNative.SetWindowPos(
            window.Handle,
            0,
            0,
            0,
            0,
            0,
            WindowsNative.SwpNoMove | WindowsNative.SwpNoSize | WindowsNative.SwpShowWindow);

        AttachToForegroundAndActivate(window.Handle);
        if (!IsForegroundBedrockWindow(window))
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

    private static bool IsForegroundBedrockWindow(MinecraftWindow window)
    {
        nint foregroundWindow = WindowsNative.GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return false;
        }

        if (foregroundWindow == window.Handle)
        {
            return true;
        }

        _ = WindowsNative.GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return processId != 0 &&
            WindowsProcessPackage.TryGetPackageFamilyName((int)processId, out string? packageFamilyName) &&
            BedrockPackage.FamilyNameFor(window.Target).Equals(packageFamilyName, StringComparison.Ordinal);
    }

    private static void AttachToForegroundAndActivate(nint windowHandle)
    {
        nint foregroundWindow = WindowsNative.GetForegroundWindow();
        uint foregroundThread = foregroundWindow == 0 ? 0 : WindowsNative.GetWindowThreadProcessId(foregroundWindow, out _);
        uint currentThread = WindowsNative.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
            WindowsNative.AttachThreadInput(currentThread, foregroundThread, attach: true);

        try
        {
            _ = WindowsNative.BringWindowToTop(windowHandle);
            _ = WindowsNative.SetForegroundWindow(windowHandle);
            _ = WindowsNative.SetFocus(windowHandle);
        }
        finally
        {
            if (attached)
            {
                _ = WindowsNative.AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
        }
    }
}