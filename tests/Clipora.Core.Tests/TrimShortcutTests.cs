using Clipora.Core.Models;
using Clipora.Core.Services;

namespace Clipora.Core.Tests;

public sealed class ShortcutGestureTests
{
    [Theory]
    [InlineData(" ctrl + shift + i ", ShortcutKey.I, ShortcutModifiers.Control | ShortcutModifiers.Shift, "Ctrl+Shift+I")]
    [InlineData("Control+Number0", ShortcutKey.Digit0, ShortcutModifiers.Control, "Ctrl+0")]
    [InlineData("Alt+OemComma", ShortcutKey.Comma, ShortcutModifiers.Alt, "Alt+Comma")]
    [InlineData("Shift+F12", ShortcutKey.F12, ShortcutModifiers.Shift, "Shift+F12")]
    [InlineData("Spacebar", ShortcutKey.Space, ShortcutModifiers.None, "Space")]
    [InlineData("Return", ShortcutKey.Enter, ShortcutModifiers.None, "Enter")]
    [InlineData("LeftArrow", ShortcutKey.Left, ShortcutModifiers.None, "Left")]
    [InlineData("Back", ShortcutKey.Backspace, ShortcutModifiers.None, "Backspace")]
    [InlineData("Win+O", ShortcutKey.O, ShortcutModifiers.Windows, "Win+O")]
    public void TryParse_NormalizesSupportedAliases(
        string value,
        ShortcutKey expectedKey,
        ShortcutModifiers expectedModifiers,
        string expectedDisplay)
    {
        Assert.True(ShortcutGesture.TryParse(value, out ShortcutGesture gesture));
        Assert.Equal(new ShortcutGesture(expectedKey, expectedModifiers), gesture);
        Assert.Equal(expectedDisplay, gesture.ToDisplayString());
        Assert.Equal(expectedDisplay, gesture.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+I")]
    [InlineData("I+Ctrl")]
    [InlineData("Ctrl+Unknown")]
    public void TryParse_RejectsIncompleteOrUnknownGesture(string? value)
    {
        Assert.False(ShortcutGesture.TryParse(value, out _));
    }

    [Fact]
    public void DisplayAndParser_RoundTripEveryAssignableKey()
    {
        foreach (ShortcutKey key in Enum.GetValues<ShortcutKey>())
        {
            ShortcutGesture gesture = new(
                key,
                ShortcutModifiers.Control | ShortcutModifiers.Shift);
            if (!TrimShortcutPolicy.IsAssignable(gesture, out _))
            {
                continue;
            }

            string display = gesture.ToDisplayString();
            Assert.True(ShortcutGesture.TryParse(display, out ShortcutGesture parsed), display);
            Assert.Equal(gesture, parsed);
        }
    }
}

public sealed class TrimShortcutPolicyTests
{
    [Fact]
    public void TryNormalize_AcceptsDefaults()
    {
        Assert.True(TrimShortcutPolicy.TryNormalize(
            TrimShortcutBindings.Default,
            out TrimShortcutBindings normalized,
            out TrimShortcutValidationError error));
        Assert.Equal(TrimShortcutValidationError.None, error);
        Assert.Equal(TrimShortcutBindings.Default, normalized);
    }

    [Theory]
    [InlineData(ShortcutKey.F4, ShortcutModifiers.Alt)]
    [InlineData(ShortcutKey.Space, ShortcutModifiers.Alt)]
    [InlineData(ShortcutKey.Escape, ShortcutModifiers.Control)]
    [InlineData(ShortcutKey.Escape, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
    public void IsAssignable_RejectsReservedSystemGestures(
        ShortcutKey key,
        ShortcutModifiers modifiers)
    {
        Assert.False(TrimShortcutPolicy.IsAssignable(
            new ShortcutGesture(key, modifiers),
            out TrimShortcutValidationError error));
        Assert.Equal(TrimShortcutValidationError.ReservedGesture, error);
    }

    [Theory]
    [InlineData(ShortcutKey.Enter)]
    [InlineData(ShortcutKey.Home)]
    [InlineData(ShortcutKey.End)]
    [InlineData(ShortcutKey.Left)]
    [InlineData(ShortcutKey.Delete)]
    [InlineData(ShortcutKey.Backspace)]
    [InlineData(ShortcutKey.Tab)]
    public void IsAssignable_RejectsFocusOrTimelineNavigationKeys(ShortcutKey key)
    {
        Assert.False(TrimShortcutPolicy.IsAssignable(
            new ShortcutGesture(key),
            out TrimShortcutValidationError error));
        Assert.Equal(TrimShortcutValidationError.UnassignableKey, error);
    }

    [Fact]
    public void IsAssignable_RejectsWindowsAndUnknownModifiers()
    {
        Assert.False(TrimShortcutPolicy.IsAssignable(
            new ShortcutGesture(ShortcutKey.I, ShortcutModifiers.Windows),
            out TrimShortcutValidationError reservedError));
        Assert.Equal(TrimShortcutValidationError.ReservedModifier, reservedError);

        Assert.False(TrimShortcutPolicy.IsAssignable(
            new ShortcutGesture(ShortcutKey.I, (ShortcutModifiers)16),
            out TrimShortcutValidationError unsupportedError));
        Assert.Equal(TrimShortcutValidationError.UnsupportedModifiers, unsupportedError);
    }

    [Fact]
    public void TryNormalize_RejectsMissingUnsupportedAndDuplicateGestures()
    {
        TrimShortcutBindings missing = TrimShortcutBindings.Default with
        {
            SetStart = default,
        };
        Assert.False(TrimShortcutPolicy.TryNormalize(missing, out _, out TrimShortcutValidationError missingError));
        Assert.Equal(TrimShortcutValidationError.MissingGesture, missingError);

        TrimShortcutBindings unsupported = TrimShortcutBindings.Default with
        {
            SetStart = new ShortcutGesture((ShortcutKey)999),
        };
        Assert.False(TrimShortcutPolicy.TryNormalize(unsupported, out _, out TrimShortcutValidationError keyError));
        Assert.Equal(TrimShortcutValidationError.UnsupportedKey, keyError);

        TrimShortcutBindings duplicate = TrimShortcutBindings.Default with
        {
            SetStart = TrimShortcutBindings.Default.PlayPause,
        };
        Assert.False(TrimShortcutPolicy.TryNormalize(duplicate, out _, out TrimShortcutValidationError duplicateError));
        Assert.Equal(TrimShortcutValidationError.DuplicateGesture, duplicateError);
    }

    [Fact]
    public void NormalizeOrDefault_ReplacesNullOrInvalidBindings()
    {
        Assert.Equal(TrimShortcutBindings.Default, TrimShortcutPolicy.NormalizeOrDefault(null));
        Assert.Equal(
            TrimShortcutBindings.Default,
            TrimShortcutPolicy.NormalizeOrDefault(TrimShortcutBindings.Default with
            {
                PlayPause = new ShortcutGesture(ShortcutKey.I),
            }));
    }
}

public sealed class TrimShortcutServiceTests
{
    [Fact]
    public void TryApply_UpdatesRuntimeStateAndRaisesOnlyForChanges()
    {
        TrimShortcutService service = new();
        int changedCount = 0;
        service.BindingsChanged += (_, _) => changedCount++;
        TrimShortcutBindings bindings = new()
        {
            PlayPause = new ShortcutGesture(ShortcutKey.P, ShortcutModifiers.Control),
            SetStart = new ShortcutGesture(ShortcutKey.OpenBracket),
            SetEnd = new ShortcutGesture(ShortcutKey.CloseBracket),
        };

        Assert.True(service.TryApply(bindings, out TrimShortcutValidationError error));
        Assert.Equal(TrimShortcutValidationError.None, error);
        Assert.Equal(bindings, service.Current);
        Assert.Equal(1, changedCount);
        Assert.True(service.Matches(TrimShortcutAction.PlayPause, bindings.PlayPause));
        Assert.True(service.Matches(TrimShortcutAction.SetStart, bindings.SetStart));
        Assert.True(service.Matches(TrimShortcutAction.SetEnd, bindings.SetEnd));

        Assert.True(service.TryApply(bindings, out error));
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void TryApply_LeavesRuntimeStateUntouchedWhenBindingsAreInvalid()
    {
        TrimShortcutService service = new();
        TrimShortcutBindings before = service.Current;
        int changedCount = 0;
        service.BindingsChanged += (_, _) => changedCount++;
        TrimShortcutBindings invalid = before with
        {
            SetStart = before.PlayPause,
        };

        Assert.False(service.TryApply(invalid, out TrimShortcutValidationError error));
        Assert.Equal(TrimShortcutValidationError.DuplicateGesture, error);
        Assert.Equal(before, service.Current);
        Assert.Equal(0, changedCount);
    }
}
