using RunMc;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;
using global::Windows.Win32.System.Threading;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace RunMc.Windows;

public sealed class WindowController : IWindowController
{
    public Task BringToForegroundAsync(TargetWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        HWND hwnd = new(window.Handle);

        _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        _ = PInvoke.SetForegroundWindow(hwnd);

        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

        if (IsForegroundTargetWindow(window))
        {
            return Task.CompletedTask;
        }

        AttachToForegroundAndActivate(window.Handle);
        if (!IsForegroundTargetWindow(window))
        {
            throw new RunMcException("Target window could not be focused.");
        }

        return Task.CompletedTask;
    }

    public Task<WindowBounds> GetBoundsAsync(TargetWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GetDwmExtendedFrameBounds(window));
    }

    public Task ResizeAsync(TargetWindow window, int width, int height, CancellationToken cancellationToken)
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

    private static WindowBounds GetBounds(TargetWindow window)
    {
        if (!PInvoke.GetWindowRect(new HWND(window.Handle), out RECT rect))
        {
            throw new RunMcException("Target window bounds could not be read.");
        }

        return new WindowBounds(rect.left, rect.top, rect.Width, rect.Height);
    }

    private static WindowBounds GetDwmExtendedFrameBounds(TargetWindow window)
    {
        int result = GetDwmExtendedFrameBounds(new HWND(window.Handle), out RECT rect);
        if (result is not 0)
        {
            throw new RunMcException("Target window bounds could not be read.");
        }

        return new WindowBounds(rect.left, rect.top, rect.Width, rect.Height);
    }

    private static bool IsForegroundTargetWindow(TargetWindow window)
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
            ProcessPackage.TryGetPackageFamilyName((int)processId, out string? packageFamilyName) &&
            window.Application.PackageFamilyName.Equals(packageFamilyName, StringComparison.Ordinal);
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