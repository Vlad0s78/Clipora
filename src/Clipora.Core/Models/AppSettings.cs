namespace Clipora.Core.Models;

public sealed record AppSettings
{
    public string? Language { get; init; }

    public OutputMode OutputMode { get; init; } = OutputMode.SameFolder;

    public string? CustomOutputFolder { get; init; }

    public bool ExplorerIntegration { get; init; }

    public bool ShowTechnicalLog { get; init; }

    public TrimShortcutBindings TrimShortcuts { get; init; } = TrimShortcutBindings.Default;

    public WindowLayout? WindowLayout { get; init; }
}
