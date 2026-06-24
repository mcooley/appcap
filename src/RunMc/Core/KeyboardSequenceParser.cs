using System.Text;

namespace RunMc;

public static class KeyboardSequenceParser
{
    public static bool TryParse(string sequence, out IReadOnlyList<KeyboardAction> actions, out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        List<KeyboardAction> parsedActions = [];
        StringBuilder text = new();

        for (int index = 0; index < sequence.Length; index++)
        {
            char current = sequence[index];
            if (current == '[')
            {
                if (index + 1 < sequence.Length && sequence[index + 1] == '[')
                {
                    text.Append('[');
                    index++;
                    continue;
                }

                FlushText(parsedActions, text);
                int end = sequence.IndexOf(']', index + 1);
                if (end < 0)
                {
                    actions = [];
                    errorMessage = "Keyboard sequence has an unmatched '['.";
                    return false;
                }

                string token = sequence[(index + 1)..end];
                if (!TryParseKeyToken(token, out KeyPressKeyboardAction? action, out errorMessage))
                {
                    actions = [];
                    return false;
                }

                parsedActions.Add(action);
                index = end;
                continue;
            }

            if (current == ']' && index + 1 < sequence.Length && sequence[index + 1] == ']')
            {
                text.Append(']');
                index++;
                continue;
            }

            text.Append(current);
        }

        FlushText(parsedActions, text);
        actions = parsedActions;
        errorMessage = null;
        return true;
    }

    private static bool TryParseKeyToken(string token, out KeyPressKeyboardAction action, out string? errorMessage)
    {
        action = new KeyPressKeyboardAction([], KeyboardKey.Enter);
        if (string.IsNullOrWhiteSpace(token))
        {
            errorMessage = "Keyboard sequence has an empty key token.";
            return false;
        }

        string[] parts = token.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            errorMessage = "Keyboard sequence has an empty key token.";
            return false;
        }

        List<KeyboardModifier> modifiers = [];
        for (int index = 0; index < parts.Length - 1; index++)
        {
            if (!TryParseModifier(parts[index], out KeyboardModifier modifier))
            {
                errorMessage = $"Unknown keyboard modifier '{parts[index]}'.";
                return false;
            }

            modifiers.Add(modifier);
        }

        if (!TryParseKey(parts[^1], out KeyboardKey key))
        {
            errorMessage = $"Unknown keyboard key '{parts[^1]}'.";
            return false;
        }

        action = new KeyPressKeyboardAction(modifiers, key);
        errorMessage = null;
        return true;
    }

    private static bool TryParseModifier(string value, out KeyboardModifier modifier)
    {
        modifier = value.ToLowerInvariant() switch
        {
            "shift" => KeyboardModifier.Shift,
            "control" or "ctrl" => KeyboardModifier.Control,
            "alt" => KeyboardModifier.Alt,
            "windows" or "win" => KeyboardModifier.Windows,
            _ => default,
        };
        return value.Equals("shift", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("control", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("alt", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("windows", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("win", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseKey(string value, out KeyboardKey key)
    {
        key = value.ToLowerInvariant() switch
        {
            "escape" or "esc" => KeyboardKey.Escape,
            "enter" or "return" => KeyboardKey.Enter,
            "tab" => KeyboardKey.Tab,
            "backspace" => KeyboardKey.Backspace,
            "delete" or "del" => KeyboardKey.Delete,
            "insert" or "ins" => KeyboardKey.Insert,
            "home" => KeyboardKey.Home,
            "end" => KeyboardKey.End,
            "pageup" or "pgup" => KeyboardKey.PageUp,
            "pagedown" or "pgdn" => KeyboardKey.PageDown,
            "arrowup" or "up" => KeyboardKey.ArrowUp,
            "arrowdown" or "down" => KeyboardKey.ArrowDown,
            "arrowleft" or "left" => KeyboardKey.ArrowLeft,
            "arrowright" or "right" => KeyboardKey.ArrowRight,
            "space" => KeyboardKey.Space,
            _ => default,
        };

        if (IsNamedKey(value))
        {
            return true;
        }

        if (value.Length == 1 && char.IsAsciiLetter(value[0]))
        {
            key = KeyboardKey.A + (char.ToUpperInvariant(value[0]) - 'A');
            return true;
        }

        if (value.Length == 1 && char.IsAsciiDigit(value[0]))
        {
            key = KeyboardKey.Digit0 + (value[0] - '0');
            return true;
        }

        if (value.Length >= 2 && (value[0] is 'f' or 'F') && int.TryParse(value[1..], out int functionKey) && functionKey is >= 1 and <= 24)
        {
            key = KeyboardKey.F1 + (functionKey - 1);
            return true;
        }

        return false;
    }

    private static bool IsNamedKey(string value) =>
        value.Equals("escape", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("esc", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("enter", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("return", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("tab", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("backspace", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("delete", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("del", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("insert", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("ins", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("home", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("end", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("pageup", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("pgup", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("pagedown", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("pgdn", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("arrowup", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("up", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("arrowdown", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("down", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("arrowleft", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("left", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("arrowright", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("right", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("space", StringComparison.OrdinalIgnoreCase);

    private static void FlushText(List<KeyboardAction> actions, StringBuilder text)
    {
        if (text.Length > 0)
        {
            actions.Add(new TextKeyboardAction(text.ToString()));
            text.Clear();
        }
    }
}