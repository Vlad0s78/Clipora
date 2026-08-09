using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.Core.Services;

public sealed class TrimShortcutService : ITrimShortcutService
{
    private readonly object _syncRoot = new();
    private TrimShortcutBindings _current = TrimShortcutBindings.Default;

    public TrimShortcutBindings Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public event EventHandler? BindingsChanged;

    public bool TryApply(
        TrimShortcutBindings? bindings,
        out TrimShortcutValidationError error)
    {
        if (!TrimShortcutPolicy.TryNormalize(bindings, out TrimShortcutBindings normalized, out error))
        {
            return false;
        }

        EventHandler? changed;
        lock (_syncRoot)
        {
            if (_current == normalized)
            {
                return true;
            }

            _current = normalized;
            changed = BindingsChanged;
        }

        changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Matches(TrimShortcutAction action, ShortcutGesture gesture)
    {
        return Current.Get(action) == gesture;
    }
}
