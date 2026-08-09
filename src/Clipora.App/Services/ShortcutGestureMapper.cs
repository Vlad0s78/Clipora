using Clipora.Core.Models;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Clipora.App.Services;

public static class ShortcutGestureMapper
{
    private const int OemSemicolon = 0xBA;
    private const int OemPlus = 0xBB;
    private const int OemComma = 0xBC;
    private const int OemMinus = 0xBD;
    private const int OemPeriod = 0xBE;
    private const int OemSlash = 0xBF;
    private const int OemBacktick = 0xC0;
    private const int OemOpenBracket = 0xDB;
    private const int OemBackslash = 0xDC;
    private const int OemCloseBracket = 0xDD;
    private const int OemQuote = 0xDE;

    public static ShortcutModifiers GetPressedModifiers()
    {
        ShortcutModifiers modifiers = ShortcutModifiers.None;
        if (IsKeyDown(VirtualKey.Control) ||
            IsKeyDown(VirtualKey.LeftControl) ||
            IsKeyDown(VirtualKey.RightControl))
        {
            modifiers |= ShortcutModifiers.Control;
        }

        if (IsKeyDown(VirtualKey.Menu) ||
            IsKeyDown(VirtualKey.LeftMenu) ||
            IsKeyDown(VirtualKey.RightMenu))
        {
            modifiers |= ShortcutModifiers.Alt;
        }

        if (IsKeyDown(VirtualKey.Shift) ||
            IsKeyDown(VirtualKey.LeftShift) ||
            IsKeyDown(VirtualKey.RightShift))
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            modifiers |= ShortcutModifiers.Windows;
        }

        return modifiers;
    }

    public static bool TryFromVirtualKey(
        VirtualKey key,
        ShortcutModifiers pressedModifiers,
        out ShortcutGesture gesture)
    {
        gesture = default;
        if (IsModifierKey(key) || !TryMapKey(key, out ShortcutKey shortcutKey))
        {
            return false;
        }

        gesture = new ShortcutGesture(shortcutKey, pressedModifiers);
        return true;
    }

    public static bool TryToVirtualKey(ShortcutKey key, out VirtualKey virtualKey)
    {
        int value = (int)key;
        if (value is >= (int)ShortcutKey.A and <= (int)ShortcutKey.Z)
        {
            virtualKey = (VirtualKey)((int)VirtualKey.A + value - (int)ShortcutKey.A);
            return true;
        }

        if (value is >= (int)ShortcutKey.Digit0 and <= (int)ShortcutKey.Digit9)
        {
            virtualKey = (VirtualKey)((int)VirtualKey.Number0 + value - (int)ShortcutKey.Digit0);
            return true;
        }

        if (value is >= (int)ShortcutKey.F1 and <= (int)ShortcutKey.F12)
        {
            virtualKey = (VirtualKey)((int)VirtualKey.F1 + value - (int)ShortcutKey.F1);
            return true;
        }

        virtualKey = key switch
        {
            ShortcutKey.Space => VirtualKey.Space,
            ShortcutKey.Enter => VirtualKey.Enter,
            ShortcutKey.Home => VirtualKey.Home,
            ShortcutKey.End => VirtualKey.End,
            ShortcutKey.Left => VirtualKey.Left,
            ShortcutKey.Up => VirtualKey.Up,
            ShortcutKey.Right => VirtualKey.Right,
            ShortcutKey.Down => VirtualKey.Down,
            ShortcutKey.Delete => VirtualKey.Delete,
            ShortcutKey.Backspace => VirtualKey.Back,
            ShortcutKey.Tab => VirtualKey.Tab,
            ShortcutKey.Escape => VirtualKey.Escape,
            ShortcutKey.PageUp => VirtualKey.PageUp,
            ShortcutKey.PageDown => VirtualKey.PageDown,
            ShortcutKey.Insert => VirtualKey.Insert,
            ShortcutKey.Comma => (VirtualKey)OemComma,
            ShortcutKey.Period => (VirtualKey)OemPeriod,
            ShortcutKey.Minus => (VirtualKey)OemMinus,
            ShortcutKey.Plus => (VirtualKey)OemPlus,
            ShortcutKey.Semicolon => (VirtualKey)OemSemicolon,
            ShortcutKey.Slash => (VirtualKey)OemSlash,
            ShortcutKey.Backtick => (VirtualKey)OemBacktick,
            ShortcutKey.OpenBracket => (VirtualKey)OemOpenBracket,
            ShortcutKey.Backslash => (VirtualKey)OemBackslash,
            ShortcutKey.CloseBracket => (VirtualKey)OemCloseBracket,
            ShortcutKey.Quote => (VirtualKey)OemQuote,
            _ => VirtualKey.None,
        };
        return virtualKey is not VirtualKey.None;
    }

    public static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.Control
            or VirtualKey.LeftControl
            or VirtualKey.RightControl
            or VirtualKey.Shift
            or VirtualKey.LeftShift
            or VirtualKey.RightShift
            or VirtualKey.Menu
            or VirtualKey.LeftMenu
            or VirtualKey.RightMenu
            or VirtualKey.LeftWindows
            or VirtualKey.RightWindows;
    }

    private static bool TryMapKey(VirtualKey key, out ShortcutKey shortcutKey)
    {
        int value = (int)key;
        if (value is >= (int)VirtualKey.A and <= (int)VirtualKey.Z)
        {
            shortcutKey = (ShortcutKey)((int)ShortcutKey.A + value - (int)VirtualKey.A);
            return true;
        }

        if (value is >= (int)VirtualKey.Number0 and <= (int)VirtualKey.Number9)
        {
            shortcutKey = (ShortcutKey)((int)ShortcutKey.Digit0 + value - (int)VirtualKey.Number0);
            return true;
        }

        if (value is >= (int)VirtualKey.NumberPad0 and <= (int)VirtualKey.NumberPad9)
        {
            shortcutKey = (ShortcutKey)((int)ShortcutKey.Digit0 + value - (int)VirtualKey.NumberPad0);
            return true;
        }

        if (value is >= (int)VirtualKey.F1 and <= (int)VirtualKey.F12)
        {
            shortcutKey = (ShortcutKey)((int)ShortcutKey.F1 + value - (int)VirtualKey.F1);
            return true;
        }

        shortcutKey = key switch
        {
            VirtualKey.Space => ShortcutKey.Space,
            VirtualKey.Enter => ShortcutKey.Enter,
            VirtualKey.Home => ShortcutKey.Home,
            VirtualKey.End => ShortcutKey.End,
            VirtualKey.Left => ShortcutKey.Left,
            VirtualKey.Up => ShortcutKey.Up,
            VirtualKey.Right => ShortcutKey.Right,
            VirtualKey.Down => ShortcutKey.Down,
            VirtualKey.Delete => ShortcutKey.Delete,
            VirtualKey.Back => ShortcutKey.Backspace,
            VirtualKey.Tab => ShortcutKey.Tab,
            VirtualKey.Escape => ShortcutKey.Escape,
            VirtualKey.PageUp => ShortcutKey.PageUp,
            VirtualKey.PageDown => ShortcutKey.PageDown,
            VirtualKey.Insert => ShortcutKey.Insert,
            VirtualKey.Add => ShortcutKey.Plus,
            VirtualKey.Subtract => ShortcutKey.Minus,
            VirtualKey.Decimal => ShortcutKey.Period,
            VirtualKey.Divide => ShortcutKey.Slash,
            _ => value switch
            {
                OemComma => ShortcutKey.Comma,
                OemPeriod => ShortcutKey.Period,
                OemMinus => ShortcutKey.Minus,
                OemPlus => ShortcutKey.Plus,
                OemSemicolon => ShortcutKey.Semicolon,
                OemSlash => ShortcutKey.Slash,
                OemBacktick => ShortcutKey.Backtick,
                OemOpenBracket => ShortcutKey.OpenBracket,
                OemBackslash => ShortcutKey.Backslash,
                OemCloseBracket => ShortcutKey.CloseBracket,
                OemQuote => ShortcutKey.Quote,
                _ => ShortcutKey.None,
            },
        };
        return shortcutKey is not ShortcutKey.None;
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        return (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    }
}
