using Clipora.Core;
using Clipora.Core.Models;

namespace Clipora.Core.Tests;

public sealed class OutputDirectoryPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"clipora-output-policy-{Guid.NewGuid():N}");

    public OutputDirectoryPolicyTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(OutputMode.SameFolder, false)]
    [InlineData(OutputMode.CustomFolder, false)]
    [InlineData(OutputMode.AskEveryTime, true)]
    public void RequiresPicker_ReturnsExpectedValue(OutputMode outputMode, bool expected)
    {
        Assert.Equal(expected, OutputDirectoryPolicy.RequiresPicker(outputMode));
    }

    [Fact]
    public void TryNormalizeExistingDirectory_ReturnsFullExistingPath()
    {
        Assert.True(OutputDirectoryPolicy.TryNormalizeExistingDirectory(_directory, out string? result));
        Assert.Equal(Path.GetFullPath(_directory), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-folder")]
    public void TryNormalizeExistingDirectory_RejectsInvalidPath(string? path)
    {
        Assert.False(OutputDirectoryPolicy.TryNormalizeExistingDirectory(path, out string? result));
        Assert.Null(result);
    }

    [Fact]
    public void TryNormalizeExistingDirectory_RejectsMissingDirectory()
    {
        string missing = Path.Combine(_directory, "missing");

        Assert.False(OutputDirectoryPolicy.TryNormalizeExistingDirectory(missing, out string? result));
        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory);
        }
    }
}
