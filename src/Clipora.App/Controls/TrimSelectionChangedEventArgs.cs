namespace Clipora.App.Controls;

public sealed class TrimSelectionChangedEventArgs(double startSeconds, double endSeconds) : EventArgs
{
    public double StartSeconds { get; } = startSeconds;

    public double EndSeconds { get; } = endSeconds;
}
