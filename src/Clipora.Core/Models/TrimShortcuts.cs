namespace Clipora.Core.Models;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
}

public enum ShortcutKey
{
    None = 0,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    Space,
    Enter,
    Home,
    End,
    Left,
    Up,
    Right,
    Down,
    Delete,
    Backspace,
    Tab,
    Escape,
    PageUp,
    PageDown,
    Insert,
    Comma,
    Period,
    Minus,
    Plus,
    Semicolon,
    Slash,
    Backtick,
    OpenBracket,
    Backslash,
    CloseBracket,
    Quote,
}

public readonly record struct ShortcutGesture(
    ShortcutKey Key,
    ShortcutModifiers Modifiers = ShortcutModifiers.None)
{
    public static bool TryParse(string? value, out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        ShortcutModifiers modifiers = ShortcutModifiers.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            if (!TryParseModifier(parts[index], out ShortcutModifiers modifier) ||
                (modifiers & modifier) != 0)
            {
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryParseKey(parts[^1], out ShortcutKey key))
        {
            return false;
        }

        gesture = new ShortcutGesture(key, modifiers);
        return true;
    }

    public string ToDisplayString()
    {
        if (Key is ShortcutKey.None || !Enum.IsDefined(Key))
        {
            return string.Empty;
        }

        List<string> parts = new(5);
        if ((Modifiers & ShortcutModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & ShortcutModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & ShortcutModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & ShortcutModifiers.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyDisplayName(Key));
        return string.Join('+', parts);
    }

    public override string ToString()
    {
        return ToDisplayString();
    }

    private static bool TryParseModifier(string value, out ShortcutModifiers modifier)
    {
        modifier = value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => ShortcutModifiers.Control,
            "ALT" => ShortcutModifiers.Alt,
            "SHIFT" => ShortcutModifiers.Shift,
            "WIN" or "WINDOWS" => ShortcutModifiers.Windows,
            _ => ShortcutModifiers.None,
        };
        return modifier is not ShortcutModifiers.None;
    }

    private static bool TryParseKey(string value, out ShortcutKey key)
    {
        string normalized = value.Trim();
        if (normalized.Length == 1)
        {
            char character = char.ToUpperInvariant(normalized[0]);
            if (character is >= 'A' and <= 'Z')
            {
                return Enum.TryParse(character.ToString(), out key);
            }

            if (character is >= '0' and <= '9')
            {
                return Enum.TryParse($"Digit{character}", out key);
            }
        }

        string alias = normalized.ToUpperInvariant();
        key = alias switch
        {
            "SPACEBAR" => ShortcutKey.Space,
            "RETURN" => ShortcutKey.Enter,
            "ESC" => ShortcutKey.Escape,
            "BACK" => ShortcutKey.Backspace,
            "DEL" => ShortcutKey.Delete,
            "PGUP" or "PRIOR" => ShortcutKey.PageUp,
            "PGDN" or "NEXT" => ShortcutKey.PageDown,
            "LEFTARROW" => ShortcutKey.Left,
            "UPARROW" => ShortcutKey.Up,
            "RIGHTARROW" => ShortcutKey.Right,
            "DOWNARROW" => ShortcutKey.Down,
            "OEMCOMMA" or "," => ShortcutKey.Comma,
            "OEMPERIOD" or "." => ShortcutKey.Period,
            "OEMMINUS" or "-" => ShortcutKey.Minus,
            "OEMPLUS" or "=" => ShortcutKey.Plus,
            "OEMSEMICOLON" or ";" => ShortcutKey.Semicolon,
            "OEMQUESTION" or "OEMSLASH" or "/" => ShortcutKey.Slash,
            "OEMTILDE" or "`" => ShortcutKey.Backtick,
            "OEMOPENBRACKETS" or "[" => ShortcutKey.OpenBracket,
            "OEMPIPE" or "OEMBACKSLASH" or "\\" => ShortcutKey.Backslash,
            "OEMCLOSEBRACKETS" or "]" => ShortcutKey.CloseBracket,
            "OEMQUOTES" or "'" => ShortcutKey.Quote,
            _ => ShortcutKey.None,
        };
        if (key is not ShortcutKey.None)
        {
            return true;
        }

        if (alias.StartsWith('D') && alias.Length == 2 && char.IsAsciiDigit(alias[1]))
        {
            return Enum.TryParse($"Digit{alias[1]}", out key);
        }

        if (alias.StartsWith("NUMBER", StringComparison.Ordinal) &&
            alias.Length == 7 &&
            char.IsAsciiDigit(alias[^1]))
        {
            return Enum.TryParse($"Digit{alias[^1]}", out key);
        }

        return Enum.TryParse(normalized, ignoreCase: true, out key)
            && key is not ShortcutKey.None
            && Enum.IsDefined(key);
    }

    private static string GetKeyDisplayName(ShortcutKey key)
    {
        string name = key.ToString();
        return name.StartsWith("Digit", StringComparison.Ordinal)
            ? name[5..]
            : name;
    }
}

public enum TrimShortcutAction
{
    PlayPause,
    SetStart,
    SetEnd,
}

public enum TrimShortcutValidationError
{
    None,
    MissingGesture,
    UnsupportedKey,
    UnassignableKey,
    UnsupportedModifiers,
    ReservedModifier,
    ReservedGesture,
    DuplicateGesture,
}

public sealed record TrimShortcutBindings
{
    public static TrimShortcutBindings Default { get; } = new();

    public ShortcutGesture PlayPause { get; init; } = new(ShortcutKey.Space);

    public ShortcutGesture SetStart { get; init; } = new(ShortcutKey.I);

    public ShortcutGesture SetEnd { get; init; } = new(ShortcutKey.O);

    public ShortcutGesture Get(TrimShortcutAction action)
    {
        return action switch
        {
            TrimShortcutAction.PlayPause => PlayPause,
            TrimShortcutAction.SetStart => SetStart,
            TrimShortcutAction.SetEnd => SetEnd,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }
}
