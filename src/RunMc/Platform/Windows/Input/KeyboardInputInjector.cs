using RunMc;
using global::Windows.Win32;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RunMc.Windows;

public sealed class KeyboardInputInjector : IKeyboardInputInjector
{
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
                    SendText(text.Text);
                    break;
                case KeyPressKeyboardAction keyPress:
                    SendKeyPress(keyPress);
                    break;
                default:
                    throw new RunMcException("Unsupported keyboard action.");
            }
        }

        return Task.CompletedTask;
    }

    private static void SendText(string text)
    {
        foreach (char character in text)
        {
            SendInputs([
                UnicodeInput(character, isKeyUp: false),
                UnicodeInput(character, isKeyUp: true),
            ]);
        }
    }

    private static void SendKeyPress(KeyPressKeyboardAction keyPress)
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

        SendInputs([.. inputs]);
    }

    private static void SendInputs(INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        uint sent = PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new RunMcException("Keyboard input injection failed.");
        }
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
        _ => throw new RunMcException("Unsupported keyboard modifier."),
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
        _ => throw new RunMcException("Unsupported keyboard key."),
    };
}