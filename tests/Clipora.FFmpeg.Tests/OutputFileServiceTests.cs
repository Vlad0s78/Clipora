using Clipora.Core.Models;

namespace Clipora.FFmpeg.Tests;

public sealed class OutputFileServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Clipora.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly OutputFileService _service = new();

    [Theory]
    [InlineData(EncodeMode.TrimOnly, "ролик_trimmed.tmp.mp4", "ролик_trimmed.mp4")]
    [InlineData(EncodeMode.Compress, "ролик_compressed.tmp.mp4", "ролик_compressed.mp4")]
    [InlineData(
        EncodeMode.TrimAndCompress,
        "ролик_trimmed_compressed.tmp.mp4",
        "ролик_trimmed_compressed.mp4")]
    public void CreatePlan_UsesModeSuffixAndTemporaryName(
        EncodeMode mode,
        string expectedTemporaryName,
        string expectedFinalName)
    {
        string sourcePath = CreateFile("ролик.mp4", "source");

        OutputFilePlan plan = _service.CreatePlan(sourcePath, mode);

        Assert.Equal(sourcePath, plan.SourcePath);
        Assert.Equal(Path.Combine(_temporaryDirectory, expectedTemporaryName), plan.TemporaryPath);
        Assert.Equal(Path.Combine(_temporaryDirectory, expectedFinalName), plan.FinalPath);
        Assert.False(string.Equals(
            sourcePath,
            plan.TemporaryPath,
            StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(
            sourcePath,
            plan.FinalPath,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreatePlan_ResolvesFinalAndTemporaryCollisions()
    {
        string sourcePath = CreateFile("video.mp4", "source");
        CreateFile("video_compressed.mp4", "existing final");
        CreateFile("video_compressed_2.tmp.mp4", "existing temporary");

        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);

        Assert.Equal(Path.Combine(_temporaryDirectory, "video_compressed_3.tmp.mp4"), plan.TemporaryPath);
        Assert.Equal(Path.Combine(_temporaryDirectory, "video_compressed_3.mp4"), plan.FinalPath);
    }

    [Fact]
    public void CreatePlan_ResolvesDirectoryCollisions()
    {
        string sourcePath = CreateFile("video.mp4", "source");
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "video_compressed.mp4"));
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "video_compressed_2.tmp.mp4"));

        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);

        Assert.Equal(Path.Combine(_temporaryDirectory, "video_compressed_3.tmp.mp4"), plan.TemporaryPath);
        Assert.Equal(Path.Combine(_temporaryDirectory, "video_compressed_3.mp4"), plan.FinalPath);
    }

    [Fact]
    public void CreatePlan_SupportsCustomDirectoryWithUnicodeAndSpaces()
    {
        string sourcePath = CreateFile("video.mkv", "source");
        string outputDirectory = Path.Combine(_temporaryDirectory, "Готовые видео");

        OutputFilePlan plan = _service.CreatePlan(
            sourcePath,
            EncodeMode.Compress,
            outputDirectory);

        Assert.True(Directory.Exists(outputDirectory));
        Assert.Equal(Path.Combine(outputDirectory, "video_compressed.tmp.mkv"), plan.TemporaryPath);
        Assert.Equal(Path.Combine(outputDirectory, "video_compressed.mkv"), plan.FinalPath);
    }

    [Fact]
    public async Task FinalizeAsync_MovesTemporaryFileWithoutChangingSource()
    {
        string sourcePath = CreateFile("video.mov", "immutable source");
        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);
        File.WriteAllText(plan.TemporaryPath, "encoded output");

        await _service.FinalizeAsync(plan, CancellationToken.None);

        Assert.False(File.Exists(plan.TemporaryPath));
        Assert.Equal("encoded output", File.ReadAllText(plan.FinalPath));
        Assert.Equal("immutable source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotOverwriteExistingFinalFile()
    {
        string sourcePath = CreateFile("video.mp4", "source");
        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);
        File.WriteAllText(plan.TemporaryPath, "new output");
        File.WriteAllText(plan.FinalPath, "existing output");

        await Assert.ThrowsAsync<IOException>(
            () => _service.FinalizeAsync(plan, CancellationToken.None));

        Assert.Equal("new output", File.ReadAllText(plan.TemporaryPath));
        Assert.Equal("existing output", File.ReadAllText(plan.FinalPath));
        Assert.Equal("source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task FinalizeAsync_HonorsCancellationAndLeavesTemporaryFile()
    {
        string sourcePath = CreateFile("video.mp4", "source");
        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);
        File.WriteAllText(plan.TemporaryPath, "new output");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.FinalizeAsync(plan, cancellation.Token));

        Assert.True(File.Exists(plan.TemporaryPath));
        Assert.False(File.Exists(plan.FinalPath));
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public void DeleteTemporaryFile_DeletesOnlyTemporaryFile()
    {
        string sourcePath = CreateFile("video.mp4", "source");
        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);
        File.WriteAllText(plan.TemporaryPath, "temporary");
        File.WriteAllText(plan.FinalPath, "final");

        _service.DeleteTemporaryFile(plan);

        Assert.False(File.Exists(plan.TemporaryPath));
        Assert.Equal("final", File.ReadAllText(plan.FinalPath));
        Assert.Equal("source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public void DeleteTemporaryFile_RejectsPathThatIsNotDerivedFromFinalName()
    {
        string unrelatedPath = CreateFile("unrelated.mp4", "keep");
        string finalPath = Path.Combine(_temporaryDirectory, "video_compressed.mp4");
        string sourcePath = CreateFile("source.mp4", "source");
        OutputFilePlan invalidPlan = new(sourcePath, unrelatedPath, finalPath);

        Assert.Throws<ArgumentException>(() => _service.DeleteTemporaryFile(invalidPlan));
        Assert.Equal("keep", File.ReadAllText(unrelatedPath));
    }

    [Fact]
    public void DeleteTemporaryFile_RejectsTemporaryPathInDifferentDirectory()
    {
        string sourcePath = CreateFile("source.mp4", "source");
        string otherDirectory = Path.Combine(_temporaryDirectory, "other");
        Directory.CreateDirectory(otherDirectory);
        string temporaryPath = Path.Combine(otherDirectory, "video_compressed.tmp.mp4");
        File.WriteAllText(temporaryPath, "keep");
        string finalPath = Path.Combine(_temporaryDirectory, "video_compressed.mp4");
        OutputFilePlan invalidPlan = new(sourcePath, temporaryPath, finalPath);

        Assert.Throws<ArgumentException>(() => _service.DeleteTemporaryFile(invalidPlan));

        Assert.Equal("keep", File.ReadAllText(temporaryPath));
        Assert.Equal("source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public void DeleteTemporaryFile_RejectsSourcePassedAsTemporaryPath()
    {
        string sourcePath = CreateFile("video.tmp.mp4", "immutable source");
        string finalPath = Path.Combine(_temporaryDirectory, "video.mp4");
        OutputFilePlan maliciousPlan = new(sourcePath, sourcePath, finalPath);

        Assert.Throws<ArgumentException>(() => _service.DeleteTemporaryFile(maliciousPlan));

        Assert.Equal("immutable source", File.ReadAllText(sourcePath));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public void DeleteTemporaryFile_DoesNotThrowWhenTemporaryFileIsLocked()
    {
        string sourcePath = CreateFile("video.mp4", "source");
        OutputFilePlan plan = _service.CreatePlan(sourcePath, EncodeMode.Compress);
        File.WriteAllText(plan.TemporaryPath, "temporary");

        using FileStream _ = new(plan.TemporaryPath, FileMode.Open, FileAccess.Read, FileShare.None);

        _service.DeleteTemporaryFile(plan);

        Assert.True(File.Exists(plan.TemporaryPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string CreateFile(string fileName, string contents)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
