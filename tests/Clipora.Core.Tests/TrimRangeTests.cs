using Clipora.Core.Models;

namespace Clipora.Core.Tests;

public sealed class TrimRangeTests
{
    [Fact]
    public void Constructor_CalculatesDuration()
    {
        TrimRange range = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(17));

        Assert.Equal(TimeSpan.FromSeconds(12), range.Duration);
    }

    [Fact]
    public void Constructor_RejectsInvalidRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrimRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
    }
}
