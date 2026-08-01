using AppCap;
using System.Diagnostics;
using System.Runtime.InteropServices;

using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;
using global::Windows.Win32.System.Threading;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace AppCap.Windows;

public sealed class WindowController : IWindowController
{
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ActivationPollDelay = TimeSpan.FromMilliseconds(50);

    public async Task BringToForegroundAsync(TargetWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        HWND hwnd = new(window.Handle);

        ActivateWindow(hwnd);
        if (await WaitForActiveTargetWindowAsync(window, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        AttachToThreadsAndActivate(window.Handle);
        if (await WaitForActiveTargetWindowAsync(window, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        UnlockForegroundWindow();
        AttachToThreadsAndActivate(window.Handle);
        if (await WaitForActiveTargetWindowAsync(window, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new AppCapException("Target window could not be focused.");
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
            throw new AppCapException("Requested window size is not possible.");
        }

        WindowBounds finalCaptureBounds = GetDwmExtendedFrameBounds(window);
        if (finalCaptureBounds.Width != width || finalCaptureBounds.Height != height)
        {
            throw new AppCapException("Requested window size is not possible.");
        }

        return Task.CompletedTask;
    }

    private static WindowBounds GetBounds(TargetWindow window)
    {
        if (!PInvoke.GetWindowRect(new HWND(window.Handle), out RECT rect))
        {
            throw new AppCapException("Target window bounds could not be read.");
        }

        return new WindowBounds(rect.left, rect.top, rect.Width, rect.Height);
    }

    private static WindowBounds GetDwmExtendedFrameBounds(TargetWindow window)
    {
        int result = GetDwmExtendedFrameBounds(new HWND(window.Handle), out RECT rect);
        if (result is not 0)
        {
            throw new AppCapException("Target window bounds could not be read.");
        }

        return new WindowBounds(rect.left, rect.top, rect.Width, rect.Height);
    }

    private static void ActivateWindow(HWND hwnd)
    {
        _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        _ = PInvoke.BringWindowToTop(hwnd);
        _ = PInvoke.SetForegroundWindow(hwnd);
        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    private static async Task<bool> WaitForActiveTargetWindowAsync(TargetWindow window, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsActiveTargetWindow(window))
            {
                return true;
            }

            await Task.Delay(ActivationPollDelay, cancellationToken).ConfigureAwait(false);
        }
        while (stopwatch.Elapsed < ActivationTimeout);

        return IsActiveTargetWindow(window);
    }

    private static bool IsActiveTargetWindow(TargetWindow window) =>
        IsForegroundTargetWindow(window) || IsTargetThreadWindowActive(window);

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
            ProcessPackage.TryGetApplicationUserModelId((int)processId, out string? applicationUserModelId) &&
            window.Application.Id.Equals(applicationUserModelId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTargetThreadWindowActive(TargetWindow window)
    {
        HWND hwnd = new(window.Handle);
        uint targetThread = PInvoke.GetWindowThreadProcessId(hwnd, out _);
        if (targetThread == 0)
        {
            return false;
        }

        GUITHREADINFO threadInfo = new()
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>(),
        };
        if (!PInvoke.GetGUIThreadInfo(targetThread, ref threadInfo))
        {
            return false;
        }

        return IsWindowOrRootWindow(threadInfo.hwndActive, hwnd) ||
            IsWindowOrRootWindow(threadInfo.hwndFocus, hwnd);
    }

    private static bool IsWindowOrRootWindow(HWND candidate, HWND targetWindow)
    {
        if (candidate.IsNull)
        {
            return false;
        }

        return candidate == targetWindow ||
            PInvoke.GetAncestor(candidate, GET_ANCESTOR_FLAGS.GA_ROOT) == targetWindow;
    }

    private static void AttachToThreadsAndActivate(nint windowHandle)
    {
        HWND foregroundWindow = PInvoke.GetForegroundWindow();
        uint foregroundThread = foregroundWindow.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foregroundWindow, out _);
        HWND hwnd = new(windowHandle);
        uint targetThread = PInvoke.GetWindowThreadProcessId(hwnd, out _);
        uint currentThread = PInvoke.GetCurrentThreadId();
        bool attachedToForeground = foregroundThread != 0 && foregroundThread != currentThread &&
            PInvoke.AttachThreadInput(currentThread, foregroundThread, fAttach: true);
        bool attachedToTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread &&
            PInvoke.AttachThreadInput(currentThread, targetThread, fAttach: true);

        try
        {
            ActivateWindow(hwnd);
            _ = PInvoke.BringWindowToTop(hwnd);
            _ = PInvoke.SetForegroundWindow(hwnd);
            _ = PInvoke.SetActiveWindow(hwnd);
            _ = PInvoke.SetFocus(hwnd);
        }
        finally
        {
            if (attachedToTarget)
            {
                _ = PInvoke.AttachThreadInput(currentThread, targetThread, fAttach: false);
            }

            if (attachedToForeground)
            {
                _ = PInvoke.AttachThreadInput(currentThread, foregroundThread, fAttach: false);
            }
        }
    }

    private static void UnlockForegroundWindow()
    {
        INPUT[] inputs =
        [
            VirtualKeyInput(VIRTUAL_KEY.VK_MENU, isKeyUp: false),
            VirtualKeyInput(VIRTUAL_KEY.VK_MENU, isKeyUp: true),
        ];

        _ = PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT VirtualKeyInput(VIRTUAL_KEY key, bool isKeyUp) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                dwFlags = isKeyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0,
            },
        },
    };

    private static unsafe int GetDwmExtendedFrameBounds(HWND windowHandle, out RECT rect)
    {
        rect = default;
        Span<byte> rectBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref rect, 1));
        int result = PInvoke.DwmGetWindowAttribute(
            windowHandle,
            DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            rectBytes).Value;
        return result;
    }
}