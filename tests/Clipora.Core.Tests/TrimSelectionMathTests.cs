using Clipora.Core.Models;

namespace Clipora.Core.Tests;

public sealed class TrimSelectionMathTests
{
    [Fact]
    public void Normalize_ClampsAndOrdersRange()
    {
        (double start, double end) = TrimSelectionMath.Normalize(14d, -2d, 10d);

        Assert.Equal(0d, start);
        Assert.Equal(10d, end);
    }

    [Fact]
    public void ClampHandles_PreserveMinimumSelection()
    {
        double start = TrimSelectionMath.ClampStart(9d, 5d, 10d);
        double end = TrimSelectionMath.ClampEnd(2d, 5d, 10d);

        Assert.Equal(4.9d, start, precision: 8);
        Assert.Equal(5.1d, end, precision: 8);
    }

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(double.PositiveInfinity, 0d)]
    [InlineData(-5d, 0d)]
    [InlineData(15d, 10d)]
    public void ClampPosition_HandlesInvalidAndOutOfRangeValues(double value, double expected)
    {
        Assert.Equal(expected, TrimSelectionMath.ClampPosition(value, 10d));
    }

    [Theory]
    [InlineData(1d, 3d)]
    [InlineData(3d, 3d)]
    [InlineData(5d, 5d)]
    [InlineData(7d, 7d)]
    [InlineData(9d, 7d)]
    [InlineData(double.NaN, 3d)]
    public void ClampPositionToSelection_StaysInsideExactBoundaries(double value, double expected)
    {
        Assert.Equal(expected, TrimSelectionMath.ClampPositionToSelection(value, 3d, 7d, 10d));
    }

    [Fact]
    public void ClampPositionToSelection_NormalizesReversedSelection()
    {
        Assert.Equal(4d, TrimSelectionMath.ClampPositionToSelection(4d, 7d, 3d, 10d));
    }

    [Fact]
    public void FormatTimestamp_UsesMillisecondsAndExpandsToHours()
    {
        Assert.Equal("01:02.345", TrimSelectionMath.FormatTimestamp(TimeSpan.FromMilliseconds(62_345)));
        Assert.Equal("01:02:03.004", TrimSelectionMath.FormatTimestamp(new TimeSpan(0, 1, 2, 3, 4)));
    }

    [Fact]
    public void FormatRulerLabel_UsesCompactClock()
    {
        Assert.Equal("01:30", TrimSelectionMath.FormatRulerLabel(90d));
        Assert.Equal("01:01:01", TrimSelectionMath.FormatRulerLabel(3661d));
    }
}
