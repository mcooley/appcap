using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RunMc;

public sealed class WindowsWindowController : IWindowController
{
    public Task BringToForegroundAsync(MinecraftWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        _ = PInvoke.ShowWindow(new HWND(window.Handle), SHOW_WINDOW_CMD.SW_RESTORE);
        if (IsForegroundBedrockWindow(window))
        {
            return Task.CompletedTask;
        }

        _ = PInvoke.SetForegroundWindow(new HWND(window.Handle));
        if (IsForegroundBedrockWindow(window))
        {
            return Task.CompletedTask;
        }

        _ = PInvoke.SetWindowPos(
            new HWND(window.Handle),
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

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

        return Task.FromResult(GetDwmExtendedFrameBounds(window));
    }

    public Task ResizeAsync(MinecraftWindow window, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        // We want the window's DWM extended frame bounds to match the requested width and height, since the DWM extended
        // frame bounds are what is captured in screenshots.
        WindowBounds windowBounds = GetBounds(window);
        WindowBounds captureBounds = GetDwmExtendedFrameBounds(window);
        int targetWindowWidth = windowBounds.Width + width - captureBounds.Width;
        int targetWindowHeight = windowBounds.Height + height - captureBounds.Height;

        bool moved = PInvoke.SetWindowPos(
            new HWND(window.Handle),
            HWND.Null,
            0,
            0,
            targetWindowWidth,
            targetWindowHeight,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

        if (!moved)
        {
            throw new RunMcException("Requested window size is not possible.");
        }

        WindowBounds finalCaptureBounds = GetDwmExtendedFrameBounds(window);
        if (finalCaptureBounds.Width != width || finalCaptureBounds.Height != height)
        {
            throw new RunMcException("Requested window size is not possible.");
        }

        return Task.CompletedTask;
    }

    private static WindowBounds GetBounds(MinecraftWindow window)
    {
        if (!PInvoke.GetWindowRect(new HWND(window.Handle), out RECT rect))
        {
            throw new RunMcException("Minecraft Bedrock window bounds could not be read.");
        }

        return new WindowBounds(rect.left, rect.top, rect.Width, rect.Height);
    }

    private static WindowBounds GetDwmExtendedFrameBounds(MinecraftWindow window)
    {
        int result = GetDwmExtendedFrameBounds(new HWND(window.Handle), out RECT rect);
        if (result is not 0)
        {
            throw new RunMcException("Minecraft Bedrock window bounds could not be read.");
        }

        return new WindowBounds(rect.left, rect.top, rect.Width, rect.Height);
    }

    private static bool IsForegroundBedrockWindow(MinecraftWindow window)
    {
        HWND foregroundWindow = PInvoke.GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            return false;
        }

        if (foregroundWindow == new HWND(window.Handle))
        {
            return true;
        }

        _ = PInvoke.GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return processId != 0 &&
            WindowsProcessPackage.TryGetPackageFamilyName((int)processId, out string? packageFamilyName) &&
            BedrockPackage.FamilyNameFor(window.Target).Equals(packageFamilyName, StringComparison.Ordinal);
    }

    private static void AttachToForegroundAndActivate(nint windowHandle)
    {
        HWND foregroundWindow = PInvoke.GetForegroundWindow();
        uint foregroundThread = foregroundWindow.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foregroundWindow, out _);
        uint currentThread = PInvoke.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
            PInvoke.AttachThreadInput(currentThread, foregroundThread, fAttach: true);

        try
        {
            HWND hwnd = new(windowHandle);
            _ = PInvoke.BringWindowToTop(hwnd);
            _ = PInvoke.SetForegroundWindow(hwnd);
            _ = PInvoke.SetFocus(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = PInvoke.AttachThreadInput(currentThread, foregroundThread, fAttach: false);
            }
        }
    }

    private static unsafe int GetDwmExtendedFrameBounds(HWND windowHandle, out RECT rect)
    {
        rect = default;
        RECT nativeRect = default;
        int result = PInvoke.DwmGetWindowAttribute(
            windowHandle,
            DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            &nativeRect,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<RECT>()).Value;
        rect = nativeRect;
        return result;
    }
}