namespace Clipora.App.Controls;

public sealed class TrimSeekRequestedEventArgs(double positionSeconds) : EventArgs
{
    public double PositionSeconds { get; } = positionSeconds;
}
