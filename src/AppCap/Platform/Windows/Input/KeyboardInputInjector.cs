using AppCap;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AppCap.Windows;

public sealed class KeyboardInputInjector : IKeyboardInputInjector
{
    private const uint WmChar = 0x0102;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;

    public Task TypeAsync(TargetWindow window, IReadOnlyList<KeyboardAction> actions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(actions);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (KeyboardAction action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (action)
            {
                case TextKeyboardAction text:
                    SendText(window, text.Text);
                    break;
                case KeyPressKeyboardAction keyPress:
                    SendKeyPress(window, keyPress);
                    break;
                default:
                    throw new AppCapException("Unsupported keyboard action.");
            }
        }

        return Task.CompletedTask;
    }

    private static void SendText(TargetWindow window, string text)
    {
        foreach (char character in text)
        {
            if (TrySendInputs([
                UnicodeInput(character, isKeyUp: false),
                UnicodeInput(character, isKeyUp: true),
            ]))
            {
                continue;
            }

            SendCharMessage(window, character);
        }
    }

    private static void SendKeyPress(TargetWindow window, KeyPressKeyboardAction keyPress)
    {
        List<INPUT> inputs = [];
        foreach (KeyboardModifier modifier in keyPress.Modifiers)
        {
            inputs.Add(VirtualKeyInput(VirtualKeyFor(modifier), isKeyUp: false));
        }

        VIRTUAL_KEY key = VirtualKeyFor(keyPress.Key);
        inputs.Add(VirtualKeyInput(key, isKeyUp: false));
        inputs.Add(VirtualKeyInput(key, isKeyUp: true));

        for (int index = keyPress.Modifiers.Count - 1; index >= 0; index--)
        {
            inputs.Add(VirtualKeyInput(VirtualKeyFor(keyPress.Modifiers[index]), isKeyUp: true));
        }

        if (TrySendInputs([.. inputs]))
        {
            return;
        }

        SendKeyPressMessages(window, keyPress);
    }

    private static bool TrySendInputs(INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return true;
        }

        uint sent = PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static void SendCharMessage(TargetWindow window, char character)
    {
        HWND hwnd = new(window.Handle);
        _ = PInvoke.SendMessage(hwnd, WmChar, new WPARAM((nuint)character), new LPARAM(0));
    }

    private static void SendKeyPressMessages(TargetWindow window, KeyPressKeyboardAction keyPress)
    {
        HWND hwnd = new(window.Handle);
        bool isSystemKey = keyPress.Modifiers.Contains(KeyboardModifier.Alt);
        foreach (KeyboardModifier modifier in keyPress.Modifiers)
        {
            SendKeyMessage(hwnd, VirtualKeyFor(modifier), isKeyUp: false, isSystemKey: modifier == KeyboardModifier.Alt);
        }

        VIRTUAL_KEY key = VirtualKeyFor(keyPress.Key);
        SendKeyMessage(hwnd, key, isKeyUp: false, isSystemKey);
        SendKeyMessage(hwnd, key, isKeyUp: true, isSystemKey);

        for (int index = keyPress.Modifiers.Count - 1; index >= 0; index--)
        {
            SendKeyMessage(hwnd, VirtualKeyFor(keyPress.Modifiers[index]), isKeyUp: true, isSystemKey: keyPress.Modifiers[index] == KeyboardModifier.Alt);
        }
    }

    private static void SendKeyMessage(HWND hwnd, VIRTUAL_KEY key, bool isKeyUp, bool isSystemKey)
    {
        uint message = isSystemKey
            ? (isKeyUp ? WmSysKeyUp : WmSysKeyDown)
            : (isKeyUp ? WmKeyUp : WmKeyDown);
        LPARAM lParam = new((nint)(isKeyUp ? 0xC0000001u : 0x00000001u));
        _ = PInvoke.SendMessage(hwnd, message, new WPARAM((nuint)key), lParam);
    }

    private static INPUT UnicodeInput(char character, bool isKeyUp) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = isKeyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE,
            },
        },
    };

    private static INPUT VirtualKeyInput(VIRTUAL_KEY key, bool isKeyUp) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                dwFlags = VirtualKeyFlags(key, isKeyUp),
            },
        },
    };

    private static KEYBD_EVENT_FLAGS VirtualKeyFlags(VIRTUAL_KEY key, bool isKeyUp)
    {
        KEYBD_EVENT_FLAGS flags = isKeyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (IsExtendedKey(key))
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        return flags;
    }

    private static bool IsExtendedKey(VIRTUAL_KEY key) => key is
        VIRTUAL_KEY.VK_INSERT or
        VIRTUAL_KEY.VK_DELETE or
        VIRTUAL_KEY.VK_HOME or
        VIRTUAL_KEY.VK_END or
        VIRTUAL_KEY.VK_PRIOR or
        VIRTUAL_KEY.VK_NEXT or
        VIRTUAL_KEY.VK_LEFT or
        VIRTUAL_KEY.VK_RIGHT or
        VIRTUAL_KEY.VK_UP or
        VIRTUAL_KEY.VK_DOWN or
        VIRTUAL_KEY.VK_LWIN or
        VIRTUAL_KEY.VK_RWIN;

    private static VIRTUAL_KEY VirtualKeyFor(KeyboardModifier modifier) => modifier switch
    {
        KeyboardModifier.Shift => VIRTUAL_KEY.VK_SHIFT,
        KeyboardModifier.Control => VIRTUAL_KEY.VK_CONTROL,
        KeyboardModifier.Alt => VIRTUAL_KEY.VK_MENU,
        KeyboardModifier.Windows => VIRTUAL_KEY.VK_LWIN,
        _ => throw new AppCapException("Unsupported keyboard modifier."),
    };

    private static VIRTUAL_KEY VirtualKeyFor(KeyboardKey key) => key switch
    {
        KeyboardKey.Escape => VIRTUAL_KEY.VK_ESCAPE,
        KeyboardKey.Enter => VIRTUAL_KEY.VK_RETURN,
        KeyboardKey.Tab => VIRTUAL_KEY.VK_TAB,
        KeyboardKey.Backspace => VIRTUAL_KEY.VK_BACK,
        KeyboardKey.Delete => VIRTUAL_KEY.VK_DELETE,
        KeyboardKey.Insert => VIRTUAL_KEY.VK_INSERT,
        KeyboardKey.Home => VIRTUAL_KEY.VK_HOME,
        KeyboardKey.End => VIRTUAL_KEY.VK_END,
        KeyboardKey.PageUp => VIRTUAL_KEY.VK_PRIOR,
        KeyboardKey.PageDown => VIRTUAL_KEY.VK_NEXT,
        KeyboardKey.ArrowUp => VIRTUAL_KEY.VK_UP,
        KeyboardKey.ArrowDown => VIRTUAL_KEY.VK_DOWN,
        KeyboardKey.ArrowLeft => VIRTUAL_KEY.VK_LEFT,
        KeyboardKey.ArrowRight => VIRTUAL_KEY.VK_RIGHT,
        KeyboardKey.Space => VIRTUAL_KEY.VK_SPACE,
        >= KeyboardKey.Digit0 and <= KeyboardKey.Digit9 => (VIRTUAL_KEY)((int)VIRTUAL_KEY.VK_0 + (key - KeyboardKey.Digit0)),
        >= KeyboardKey.A and <= KeyboardKey.Z => (VIRTUAL_KEY)((int)VIRTUAL_KEY.VK_A + (key - KeyboardKey.A)),
        >= KeyboardKey.F1 and <= KeyboardKey.F24 => (VIRTUAL_KEY)((int)VIRTUAL_KEY.VK_F1 + (key - KeyboardKey.F1)),
        _ => throw new AppCapException("Unsupported keyboard key."),
    };
}