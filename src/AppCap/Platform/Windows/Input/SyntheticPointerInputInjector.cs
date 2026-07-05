using AppCap;
using System.Drawing;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Input.Pointer;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace AppCap.Windows;

public sealed class SyntheticPointerInputInjector : IInputInjector
{
    public Task ClickAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        HWND hwnd = new(window.Handle);
        if (!IsTargetAtPoint(window, hwnd, screenX, screenY))
        {
            throw new AppCapException("Click target is not visible at the requested coordinates.");
        }

        using DestroySyntheticPointerDeviceSafeHandle device = PInvoke.CreateSyntheticPointerDevice_SafeHandle(
            POINTER_INPUT_TYPE.PT_TOUCH,
            1,
            POINTER_FEEDBACK_MODE.POINTER_FEEDBACK_NONE);
        if (device.IsInvalid)
        {
            throw new AppCapException("Click input injection failed.");
        }

        POINTER_TYPE_INFO[] down = [SyntheticTouchInput(
            hwnd,
            screenX,
            screenY,
            POINTER_FLAGS.POINTER_FLAG_NEW | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT | POINTER_FLAGS.POINTER_FLAG_PRIMARY | POINTER_FLAGS.POINTER_FLAG_DOWN,
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_FIRSTBUTTON_DOWN)];
        if (!PInvoke.InjectSyntheticPointerInput(device, down))
        {
            throw new AppCapException("Click input injection failed.");
        }

        Thread.Sleep(100);

        POINTER_TYPE_INFO[] update = [SyntheticTouchInput(
            hwnd,
            screenX,
            screenY,
            POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT | POINTER_FLAGS.POINTER_FLAG_PRIMARY | POINTER_FLAGS.POINTER_FLAG_UPDATE,
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_NONE)];
        if (!PInvoke.InjectSyntheticPointerInput(device, update))
        {
            throw new AppCapException("Click input injection failed.");
        }

        Thread.Sleep(50);

        POINTER_TYPE_INFO[] up = [SyntheticTouchInput(
            hwnd,
            screenX,
            screenY,
            POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_PRIMARY | POINTER_FLAGS.POINTER_FLAG_UP,
            POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_FIRSTBUTTON_UP)];
        if (!PInvoke.InjectSyntheticPointerInput(device, up))
        {
            throw new AppCapException("Click input injection failed.");
        }

        return Task.CompletedTask;
    }

    private static POINTER_TYPE_INFO SyntheticTouchInput(HWND targetWindow, int screenX, int screenY, POINTER_FLAGS pointerFlags, POINTER_BUTTON_CHANGE_TYPE buttonChangeType)
    {
        POINTER_TYPE_INFO input = new()
        {
            type = POINTER_INPUT_TYPE.PT_TOUCH,
        };
        input.touchInfo = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = POINTER_INPUT_TYPE.PT_TOUCH,
                pointerId = 1,
                pointerFlags = pointerFlags,
                hwndTarget = targetWindow,
                ptPixelLocation = new Point(screenX, screenY),
                ptPixelLocationRaw = new Point(screenX, screenY),
                ButtonChangeType = buttonChangeType,
            },
            touchMask = PInvoke.TOUCH_MASK_CONTACTAREA | PInvoke.TOUCH_MASK_PRESSURE,
            rcContact = RECT.FromXYWH(screenX - 2, screenY - 2, 4, 4),
            rcContactRaw = RECT.FromXYWH(screenX - 2, screenY - 2, 4, 4),
            pressure = pointerFlags.HasFlag(POINTER_FLAGS.POINTER_FLAG_INCONTACT) ? 512u : 0u,
        };
        return input;
    }

    private static bool IsTargetAtPoint(TargetWindow window, HWND targetWindow, int screenX, int screenY)
    {
        HWND pointWindow = PInvoke.WindowFromPoint(new Point(screenX, screenY));
        if (pointWindow.IsNull)
        {
            return false;
        }

        HWND rootWindow = PInvoke.GetAncestor(pointWindow, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (pointWindow == targetWindow)
        {
            return true;
        }

        if (rootWindow == targetWindow)
        {
            return true;
        }

        _ = PInvoke.GetWindowThreadProcessId(pointWindow, out uint processId);
        bool sameApplication = processId != 0 &&
            ProcessPackage.TryGetApplicationUserModelId((int)processId, out string? applicationUserModelId) &&
            window.Application.Id.Equals(applicationUserModelId, StringComparison.Ordinal);
        return sameApplication;
    }
}