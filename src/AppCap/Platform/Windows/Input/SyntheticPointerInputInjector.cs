using AppCap;
using System.Drawing;
using System.Runtime.InteropServices;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Input.Pointer;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace AppCap.Windows;

public sealed partial class SyntheticPointerInputInjector : IInputInjector
{
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint PtTouchpad = 5;
    private const uint PointerFeedbackNone = 3;
    private const uint SdcoPhysicalSize = 1;
    private const int TouchpadWidth = 10_000;
    private const int TouchpadHeight = 6_000;
    private const int TouchpadCenterX = TouchpadWidth / 2;
    private const int TouchpadCenterY = TouchpadHeight / 2;
    private const uint PointerFlagInRange = 0x00000002;
    private const uint PointerFlagInContact = 0x00000004;
    private const uint PointerFlagFirstButton = 0x00000010;
    private const uint PointerFlagConfidence = 0x00004000;
    private const uint PointerChangeNone = 0;
    private const uint PointerChangeFirstButtonDown = 1;
    private const uint PointerChangeFirstButtonUp = 2;

    public Task TapAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        HWND hwnd = new(window.Handle);
        if (!IsTargetAtPoint(window, hwnd, screenX, screenY))
        {
            throw new AppCapException("Tap target is not visible at the requested coordinates.");
        }

        using DestroySyntheticPointerDeviceSafeHandle device = PInvoke.CreateSyntheticPointerDevice_SafeHandle(
            POINTER_INPUT_TYPE.PT_TOUCH,
            1,
            POINTER_FEEDBACK_MODE.POINTER_FEEDBACK_NONE);
        if (!device.IsInvalid)
        {
            POINTER_TYPE_INFO[] down = [SyntheticTouchInput(
                hwnd,
                screenX,
                screenY,
                POINTER_FLAGS.POINTER_FLAG_NEW | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT | POINTER_FLAGS.POINTER_FLAG_PRIMARY | POINTER_FLAGS.POINTER_FLAG_DOWN,
                POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_FIRSTBUTTON_DOWN)];
            if (PInvoke.InjectSyntheticPointerInput(device, down))
            {
                Thread.Sleep(100);

                POINTER_TYPE_INFO[] update = [SyntheticTouchInput(
                    hwnd,
                    screenX,
                    screenY,
                    POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT | POINTER_FLAGS.POINTER_FLAG_PRIMARY | POINTER_FLAGS.POINTER_FLAG_UPDATE,
                    POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_NONE)];
                if (PInvoke.InjectSyntheticPointerInput(device, update))
                {
                    Thread.Sleep(50);

                    POINTER_TYPE_INFO[] up = [SyntheticTouchInput(
                        hwnd,
                        screenX,
                        screenY,
                        POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_PRIMARY | POINTER_FLAGS.POINTER_FLAG_UP,
                        POINTER_BUTTON_CHANGE_TYPE.POINTER_CHANGE_FIRSTBUTTON_UP)];
                    if (PInvoke.InjectSyntheticPointerInput(device, up))
                    {
                        return Task.CompletedTask;
                    }
                }
            }
        }

        Point clientPoint = new(screenX, screenY);
        if (!PInvoke.ScreenToClient(hwnd, ref clientPoint))
        {
            throw new AppCapException("Tap input injection failed.");
        }

        LPARAM lParam = new((clientPoint.X & 0xffff) | ((clientPoint.Y & 0xffff) << 16));
        _ = PInvoke.SendMessage(hwnd, WmMouseMove, new WPARAM(0), lParam);
        _ = PInvoke.SendMessage(hwnd, WmLButtonDown, new WPARAM(0x0001), lParam);
        _ = PInvoke.SendMessage(hwnd, WmLButtonUp, new WPARAM(0), lParam);
        return Task.CompletedTask;
    }

    public async Task MoveMouseAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        HWND hwnd = new(window.Handle);
        if (!IsTargetAtPoint(window, hwnd, screenX, screenY))
        {
            throw new AppCapException("Mouse target is not visible at the requested coordinates.");
        }

        nint device = CreateTouchpadDevice();
        try
        {
            await MoveCursorAsync(device, screenX, screenY, new TouchpadInjectionState(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DestroySyntheticPointerDevice2(device);
        }
    }

    public async Task ClickMouseAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        HWND hwnd = new(window.Handle);
        if (!IsTargetAtPoint(window, hwnd, screenX, screenY))
        {
            throw new AppCapException("Mouse target is not visible at the requested coordinates.");
        }

        nint device = CreateTouchpadDevice();
        try
        {
            TouchpadInjectionState state = new();
            await MoveCursorAsync(device, screenX, screenY, state, cancellationToken).ConfigureAwait(false);

            PointerTypeInfo input = CreateTouchpadContact();
            InjectTouchpadContact(device, ref input, TouchpadCenterX, TouchpadCenterY,
                PointerFlagInRange | PointerFlagInContact | PointerFlagConfidence,
                PointerChangeNone, state);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            InjectTouchpadContact(device, ref input, TouchpadCenterX, TouchpadCenterY,
                PointerFlagInRange | PointerFlagInContact | PointerFlagFirstButton | PointerFlagConfidence,
                PointerChangeFirstButtonDown, state);
            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            InjectTouchpadContact(device, ref input, TouchpadCenterX, TouchpadCenterY,
                PointerFlagInRange | PointerFlagInContact | PointerFlagConfidence,
                PointerChangeFirstButtonUp, state);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            InjectTouchpadContact(device, ref input, TouchpadCenterX, TouchpadCenterY,
                PointerFlagConfidence, PointerChangeNone, state);
        }
        finally
        {
            DestroySyntheticPointerDevice2(device);
        }
    }

    private static nint CreateTouchpadDevice()
    {
        SyntheticDeviceCreationParams parameters = new()
        {
            PointerType = PtTouchpad,
            MaxCount = 1,
            FeedbackMode = PointerFeedbackNone,
            DeviceWidth = TouchpadWidth,
            DeviceHeight = TouchpadHeight,
            Options = SdcoPhysicalSize,
        };
        nint device = CreateSyntheticPointerDevice2(ref parameters);
        if (device == 0)
        {
            throw new AppCapException($"Could not create a synthetic touchpad device (error {Marshal.GetLastPInvokeError()}).");
        }

        return device;
    }

    private static async Task MoveCursorAsync(nint device, int targetX, int targetY, TouchpadInjectionState state, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PInvoke.GetCursorPos(out Point cursor))
            {
                throw new AppCapException("Could not determine the current mouse position.");
            }

            int deltaX = targetX - cursor.X;
            int deltaY = targetY - cursor.Y;
            if (Math.Abs(deltaX) <= 2 && Math.Abs(deltaY) <= 2)
            {
                return;
            }

            int travelX = TouchpadTravel(deltaX, 4_000);
            int travelY = TouchpadTravel(deltaY, 2_000);
            PointerTypeInfo input = CreateTouchpadContact();
            InjectTouchpadContact(device, ref input, TouchpadCenterX, TouchpadCenterY,
                PointerFlagInRange | PointerFlagInContact | PointerFlagConfidence,
                PointerChangeNone, state);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            for (int frame = 1; frame <= 10; frame++)
            {
                InjectTouchpadContact(
                    device,
                    ref input,
                    TouchpadCenterX + (travelX * frame / 10),
                    TouchpadCenterY + (travelY * frame / 10),
                    PointerFlagInRange | PointerFlagInContact | PointerFlagConfidence,
                    PointerChangeNone,
                    state);
                await Task.Delay(8, cancellationToken).ConfigureAwait(false);
            }

            InjectTouchpadContact(device, ref input, TouchpadCenterX + travelX, TouchpadCenterY + travelY,
                PointerFlagConfidence, PointerChangeNone, state);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        throw new AppCapException("Synthetic touchpad input did not move the mouse to the requested coordinates.");
    }

    private static int TouchpadTravel(int pixelDelta, int maximumTravel)
    {
        int scale = Math.Abs(pixelDelta) <= 20 ? 20 : 10;
        return Math.Clamp(pixelDelta * scale, -maximumTravel, maximumTravel);
    }

    private static PointerTypeInfo CreateTouchpadContact()
    {
        return new PointerTypeInfo
        {
            Type = PtTouchpad,
            TouchInfo = new PointerTouchInfo
            {
                PointerInfo = new PointerInfo
                {
                    PointerType = PtTouchpad,
                    PointerId = 0,
                },
            },
        };
    }

    private static unsafe void InjectTouchpadContact(
        nint device,
        ref PointerTypeInfo input,
        int x,
        int y,
        uint flags,
        uint buttonChangeType,
        TouchpadInjectionState state)
    {
        input.TouchInfo.PointerInfo.PointerFlags = flags;
        input.TouchInfo.PointerInfo.HimetricLocation = new NativePoint(x, y);
        input.TouchInfo.PointerInfo.HimetricLocationRaw = new NativePoint(x, y);
        input.TouchInfo.PointerInfo.Time = NextTouchpadTimestamp(state);
        input.TouchInfo.PointerInfo.ButtonChangeType = buttonChangeType;

        fixed (PointerTypeInfo* pointerInfo = &input)
        {
            if (InjectSyntheticPointerInput2(device, pointerInfo, 1) == 0)
            {
                throw new AppCapException($"Synthetic touchpad input injection failed (error {Marshal.GetLastPInvokeError()}).");
            }
        }
    }

    private static uint NextTouchpadTimestamp(TouchpadInjectionState state)
    {
        uint timestamp = GetTickCount();
        if (state.PreviousTimestamp != 0 && unchecked((int)(timestamp - state.PreviousTimestamp)) <= 0)
        {
            timestamp = state.PreviousTimestamp + 1;
        }

        state.PreviousTimestamp = timestamp;
        return timestamp;
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
            window.Application.Id.Equals(applicationUserModelId, StringComparison.OrdinalIgnoreCase);
        return sameApplication;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateSyntheticPointerDevice2", SetLastError = true)]
    private static partial nint CreateSyntheticPointerDevice2(ref SyntheticDeviceCreationParams parameters);

    [LibraryImport("user32.dll", EntryPoint = "InjectSyntheticPointerInput", SetLastError = true)]
    private static unsafe partial int InjectSyntheticPointerInput2(nint device, PointerTypeInfo* pointerInfo, uint count);

    [LibraryImport("user32.dll", EntryPoint = "DestroySyntheticPointerDevice")]
    private static partial void DestroySyntheticPointerDevice2(nint device);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetTickCount();

    private sealed class TouchpadInjectionState
    {
        public uint PreviousTimestamp { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SyntheticDeviceCreationParams
    {
        public uint PointerType;
        public uint MaxCount;
        public uint FeedbackMode;
        public nint Monitor;
        public uint DeviceWidth;
        public uint DeviceHeight;
        public uint Options;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PointerTypeInfo
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public PointerTouchInfo TouchInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerTouchInfo
    {
        public PointerInfo PointerInfo;
        public uint TouchFlags;
        public uint TouchMask;
        public NativeRectangle Contact;
        public NativeRectangle ContactRaw;
        public uint Orientation;
        public uint Pressure;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public uint PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public nint SourceDevice;
        public nint TargetWindow;
        public NativePoint PixelLocation;
        public NativePoint HimetricLocation;
        public NativePoint PixelLocationRaw;
        public NativePoint HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}