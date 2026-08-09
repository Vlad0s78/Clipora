using Clipora.Core.Models;

namespace Clipora.Core.Services;

public static class TrimShortcutPolicy
{
    private const ShortcutModifiers SupportedModifiers =
        ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift;
    private const ShortcutModifiers KnownModifiers =
        SupportedModifiers | ShortcutModifiers.Windows;

    public static bool TryNormalize(
        TrimShortcutBindings? bindings,
        out TrimShortcutBindings normalized,
        out TrimShortcutValidationError error)
    {
        normalized = TrimShortcutBindings.Default;
        error = TrimShortcutValidationError.None;
        if (bindings is null)
        {
            error = TrimShortcutValidationError.MissingGesture;
            return false;
        }

        ShortcutGesture[] gestures =
        [
            bindings.PlayPause,
            bindings.SetStart,
            bindings.SetEnd,
        ];
        foreach (ShortcutGesture gesture in gestures)
        {
            if (!IsAssignable(gesture, out error))
            {
                return false;
            }
        }

        if (gestures.Distinct().Count() != gestures.Length)
        {
            error = TrimShortcutValidationError.DuplicateGesture;
            return false;
        }

        normalized = bindings with
        {
            PlayPause = new ShortcutGesture(bindings.PlayPause.Key, bindings.PlayPause.Modifiers & SupportedModifiers),
            SetStart = new ShortcutGesture(bindings.SetStart.Key, bindings.SetStart.Modifiers & SupportedModifiers),
            SetEnd = new ShortcutGesture(bindings.SetEnd.Key, bindings.SetEnd.Modifiers & SupportedModifiers),
        };
        return true;
    }

    public static bool IsAssignable(
        ShortcutGesture gesture,
        out TrimShortcutValidationError error)
    {
        error = TrimShortcutValidationError.None;
        if (gesture.Key is ShortcutKey.None)
        {
            error = TrimShortcutValidationError.MissingGesture;
            return false;
        }

        if (!Enum.IsDefined(gesture.Key))
        {
            error = TrimShortcutValidationError.UnsupportedKey;
            return false;
        }

        if ((gesture.Modifiers & ~KnownModifiers) != 0)
        {
            error = TrimShortcutValidationError.UnsupportedModifiers;
            return false;
        }

        if ((gesture.Modifiers & ShortcutModifiers.Windows) != 0)
        {
            error = TrimShortcutValidationError.ReservedModifier;
            return false;
        }

        bool hasControl = (gesture.Modifiers & ShortcutModifiers.Control) != 0;
        bool hasAlt = (gesture.Modifiers & ShortcutModifiers.Alt) != 0;
        bool reserved = hasAlt && gesture.Key is ShortcutKey.F4 or ShortcutKey.Space
            || hasControl && gesture.Key is ShortcutKey.Escape;
        if (reserved)
        {
            error = TrimShortcutValidationError.ReservedGesture;
            return false;
        }

        if (!IsAssignableKey(gesture.Key))
        {
            error = TrimShortcutValidationError.UnassignableKey;
            return false;
        }

        return true;
    }

    public static TrimShortcutBindings NormalizeOrDefault(TrimShortcutBindings? bindings)
    {
        return TryNormalize(bindings, out TrimShortcutBindings normalized, out _)
            ? normalized
            : TrimShortcutBindings.Default;
    }

    private static bool IsAssignableKey(ShortcutKey key)
    {
        int value = (int)key;
        return value is >= (int)ShortcutKey.A and <= (int)ShortcutKey.Z
            or >= (int)ShortcutKey.Digit0 and <= (int)ShortcutKey.Digit9
            or >= (int)ShortcutKey.F1 and <= (int)ShortcutKey.F12
            || key is ShortcutKey.Space
            || value is >= (int)ShortcutKey.Comma and <= (int)ShortcutKey.Quote;
    }
}
