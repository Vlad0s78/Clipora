using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface ITrimShortcutService
{
    TrimShortcutBindings Current { get; }

    event EventHandler? BindingsChanged;

    bool TryApply(
        TrimShortcutBindings? bindings,
        out TrimShortcutValidationError error);

    bool Matches(TrimShortcutAction action, ShortcutGesture gesture);
}
