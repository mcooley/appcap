using System.Runtime.InteropServices;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

return WindowApplication.Run();

internal static unsafe class WindowApplication
{
    private const int InitialWidth = 640;
    private const int InitialHeight = 480;
    private const string ClassName = "AppCapE2ETestAppWindow";
    private static readonly string Title = $"AppCap E2E Test App ({GetCurrentApplicationId()})";

    private static readonly delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> WindowProcedure = &WndProc;
    private static readonly RECT ClickRect = new() { left = 80, top = 80, right = 220, bottom = 180 };
    private static readonly RECT HoverRect = new() { left = 260, top = 80, right = 400, bottom = 180 };
    private static readonly RECT TextRect = new() { left = 80, top = 240, right = 560, bottom = 340 };

    private static bool clicked;
    private static bool hovered;
    private static readonly StringBuilder TypedText = new();
    private static bool gameInputLoaded;

    public static int Run()
    {
        _ = PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        gameInputLoaded = GameInputProbe.TryCreate();

        HINSTANCE instance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR));
        fixed (char* className = ClassName)
        fixed (char* title = Title)
        {
            WNDCLASSW windowClass = new()
            {
                lpfnWndProc = WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(default(HINSTANCE), PInvoke.IDC_ARROW),
                lpszClassName = new PCWSTR(className),
            };

            if (PInvoke.RegisterClass(windowClass) == 0)
            {
                return 1;
            }

            HWND hwnd = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_APPWINDOW,
                new PCWSTR(className),
                new PCWSTR(title),
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_VISIBLE,
                PInvoke.CW_USEDEFAULT,
                PInvoke.CW_USEDEFAULT,
                InitialWidth,
                InitialHeight,
                default,
                default,
                instance,
                null);

            if (hwnd == default)
            {
                return 1;
            }

            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);
            _ = PInvoke.UpdateWindow(hwnd);

            MSG message;
            while (PInvoke.GetMessage(out message, default, 0, 0))
            {
                _ = PInvoke.TranslateMessage(message);
                _ = PInvoke.DispatchMessage(message);
            }

            return (int)message.wParam.Value;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_PAINT:
                Paint(hwnd);
                return new LRESULT(0);
            case PInvoke.WM_SIZE:
                ResetState(hwnd);
                return new LRESULT(0);
            case PInvoke.WM_MOUSEMOVE:
                SetHovered(hwnd, GetX(lParam), GetY(lParam));
                return new LRESULT(0);
            case PInvoke.WM_POINTERUPDATE:
                Point pointerMovePoint = PointFromPointerLParam(hwnd, lParam);
                SetHovered(hwnd, pointerMovePoint.X, pointerMovePoint.Y);
                return new LRESULT(0);
            case PInvoke.WM_LBUTTONDOWN:
                SetClicked(hwnd, GetX(lParam), GetY(lParam));
                return new LRESULT(0);
            case PInvoke.WM_POINTERDOWN:
                Point pointerDownPoint = PointFromPointerLParam(hwnd, lParam);
                SetClicked(hwnd, pointerDownPoint.X, pointerDownPoint.Y);
                return new LRESULT(0);
            case PInvoke.WM_CHAR:
                TypedText.Append((char)wParam.Value);
                UpdateTitle(hwnd);
                _ = PInvoke.InvalidateRect(hwnd, null, false);
                return new LRESULT(0);
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
            default:
                return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private static void Paint(HWND hwnd)
    {
        PAINTSTRUCT paint;
        HDC hdc = PInvoke.BeginPaint(hwnd, out paint);

        Fill(hdc, paint.rcPaint, Rgb(10, 90, 140));
        Fill(hdc, ClickRect, clicked ? Rgb(220, 40, 40) : Rgb(80, 80, 80));
        Fill(hdc, HoverRect, hovered ? Rgb(245, 210, 40) : Rgb(80, 80, 80));
        Fill(hdc, TextRect, TypedText.Length > 0 ? Rgb(40, 190, 90) : Rgb(80, 80, 80));

        _ = PInvoke.SetBkMode(hdc, BACKGROUND_MODE.TRANSPARENT);
        _ = PInvoke.SetTextColor(hdc, Rgb(255, 255, 255));
        DrawText(hdc, 92, 112, clicked ? "clicked" : "click target");
        DrawText(hdc, 272, 112, hovered ? "hovered" : "hover target");
        DrawText(hdc, 92, 272, TypedText.Length > 0 ? $"typed {TypedText.Length}" : "type target");
        DrawText(hdc, 16, 16, gameInputLoaded ? "GameInput loaded" : "GameInput unavailable");

        _ = PInvoke.EndPaint(hwnd, paint);
    }

    private static void Fill(HDC hdc, RECT rect, COLORREF color)
    {
        using DeleteObjectSafeHandle brush = PInvoke.CreateSolidBrush_SafeHandle(color);
        _ = PInvoke.FillRect(hdc, rect, brush);
    }

    private static void DrawText(HDC hdc, int x, int y, string text)
    {
        fixed (char* textPointer = text)
        {
            _ = PInvoke.TextOut(hdc, x, y, new PCWSTR(textPointer), text.Length);
        }
    }

    private static void SetClicked(HWND hwnd, int x, int y)
    {
        if (!Contains(ClickRect, x, y))
        {
            return;
        }

        clicked = true;
        _ = PInvoke.InvalidateRect(hwnd, null, false);
    }

    private static void ResetState(HWND hwnd)
    {
        clicked = false;
        hovered = false;
        TypedText.Clear();
        UpdateTitle(hwnd);
        _ = PInvoke.InvalidateRect(hwnd, null, false);
    }

    private static void UpdateTitle(HWND hwnd)
    {
        string escapedText = TypedText
            .ToString()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        string title = $"{Title} | typed:{escapedText}";
        unsafe
        {
            fixed (char* titlePointer = title)
            {
                _ = PInvoke.SetWindowText(hwnd, new PCWSTR(titlePointer));
            }
        }
    }

    private static void SetHovered(HWND hwnd, int x, int y)
    {
        if (hovered || !Contains(HoverRect, x, y))
        {
            return;
        }

        hovered = true;
        _ = PInvoke.InvalidateRect(hwnd, null, false);
    }

    private static Point PointFromPointerLParam(HWND hwnd, LPARAM lParam)
    {
        Point point = new(GetX(lParam), GetY(lParam));
        _ = PInvoke.ScreenToClient(hwnd, ref point);
        return point;
    }

    private static bool Contains(RECT rect, int x, int y) =>
        x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;

    private static int GetX(LPARAM lParam) => unchecked((short)((nint)lParam.Value & 0xffff));

    private static int GetY(LPARAM lParam) => unchecked((short)(((nint)lParam.Value >> 16) & 0xffff));

    private static COLORREF Rgb(byte red, byte green, byte blue) => new(red | ((uint)green << 8) | ((uint)blue << 16));

    private static string GetCurrentApplicationId()
    {
        uint length = 0;
        WIN32_ERROR result = PInvoke.GetCurrentApplicationUserModelId(ref length, []);
        if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return "Unpackaged";
        }

        char[] buffer = new char[length];
        result = PInvoke.GetCurrentApplicationUserModelId(ref length, buffer);
        if (result != WIN32_ERROR.NO_ERROR)
        {
            return "Unpackaged";
        }

        string applicationUserModelId = new(buffer, 0, Math.Max(0, checked((int)length) - 1));
        int separator = applicationUserModelId.LastIndexOf('!');
        return separator >= 0 ? applicationUserModelId[(separator + 1)..] : applicationUserModelId;
    }
}

internal static unsafe class GameInputProbe
{
    public static bool TryCreate()
    {
        if (!NativeLibrary.TryLoad("GameInput.dll", out nint library))
        {
            return false;
        }

        if (!NativeLibrary.TryGetExport(library, "GameInputCreate", out nint export))
        {
            NativeLibrary.Free(library);
            return false;
        }

        nint gameInput = 0;
        delegate* unmanaged[Stdcall]<nint*, int> gameInputCreate = (delegate* unmanaged[Stdcall]<nint*, int>)export;
        int result = gameInputCreate(&gameInput);
        return result >= 0 && gameInput != 0;
    }
}