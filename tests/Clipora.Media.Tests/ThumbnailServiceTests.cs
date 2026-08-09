using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Clipora.Core.Tools;
using Clipora.Media;

namespace Clipora.Media.Tests;

public sealed class ThumbnailServiceTests
{
    [Fact]
    public void CreateStartInfo_UsesDirectHiddenProcessAndSingleOutputPattern()
    {
        using TemporaryDirectory temporary = new();
        string ffmpegPath = Path.Combine(temporary.Path, "ffmpeg.exe");
        string videoPath = Path.Combine(temporary.Path, "video with spaces.mp4");
        string stagingPath = Path.Combine(temporary.Path, "staging");

        ProcessStartInfo startInfo = ThumbnailService.CreateStartInfo(
            ffmpegPath,
            videoPath,
            stagingPath,
            TimeSpan.FromSeconds(80),
            20);
        string[] arguments = startInfo.ArgumentList.ToArray();

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(Path.GetFullPath(ffmpegPath), startInfo.FileName);
        Assert.Equal(1, arguments.Count(argument => argument == Path.GetFullPath(videoPath)));
        Assert.Contains("0:V:0", arguments);
        Assert.Contains("-frames:v", arguments);
        Assert.Contains("20", arguments);
        string filter = arguments[Array.IndexOf(arguments, "-vf") + 1];
        Assert.Contains("fps=fps=20/80", filter, StringComparison.Ordinal);
        Assert.Contains("scale=w=160:h=90", filter, StringComparison.Ordinal);
        Assert.Contains("force_original_aspect_ratio=increase", filter, StringComparison.Ordinal);
        Assert.Contains("crop=160:90", filter, StringComparison.Ordinal);
        Assert.Contains("setsar=1", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("force_original_aspect_ratio=decrease", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("pad=", filter, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(stagingPath, "frame-%03d.jpg"), arguments[^1]);
    }

    [Fact]
    public void CreateCacheKey_IsStableAndIncludesEverySourceAttribute()
    {
        using TemporaryDirectory temporary = new();
        string firstPath = Path.Combine(temporary.Path, "first.mp4");
        string secondPath = Path.Combine(temporary.Path, "second.mp4");
        DateTime modified = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan duration = TimeSpan.FromSeconds(60);

        string key = ThumbnailService.CreateCacheKey(firstPath, 123, modified, duration, 20);
        string legacyKey = CreateLegacyCacheKey(firstPath, 123, modified, duration, 20);

        Assert.Equal(key, ThumbnailService.CreateCacheKey(firstPath, 123, modified, duration, 20));
        Assert.Equal(64, key.Length);
        Assert.NotEqual(legacyKey, key);
        Assert.NotEqual(key, ThumbnailService.CreateCacheKey(secondPath, 123, modified, duration, 20));
        Assert.NotEqual(key, ThumbnailService.CreateCacheKey(firstPath, 124, modified, duration, 20));
        Assert.NotEqual(key, ThumbnailService.CreateCacheKey(firstPath, 123, modified.AddTicks(1), duration, 20));
        Assert.NotEqual(key, ThumbnailService.CreateCacheKey(firstPath, 123, modified, duration.Add(TimeSpan.FromTicks(1)), 20));
        Assert.NotEqual(key, ThumbnailService.CreateCacheKey(firstPath, 123, modified, duration, 21));
    }

    private static string CreateLegacyCacheKey(
        string fullVideoPath,
        long fileLength,
        DateTime lastWriteTimeUtc,
        TimeSpan duration,
        int count)
    {
        string normalizedPath = Path.GetFullPath(fullVideoPath).ToUpperInvariant();
        string value = string.Join(
            "\n",
            normalizedPath,
            fileLength.ToString(CultureInfo.InvariantCulture),
            lastWriteTimeUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            duration.Ticks.ToString(CultureInfo.InvariantCulture),
            count.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    [Fact]
    public async Task GenerateAsync_ReusesValidatedCacheWithoutResolvingFfmpeg()
    {
        using TemporaryDirectory temporary = new();
        string videoPath = Path.Combine(temporary.Path, "source.mp4");
        await File.WriteAllBytesAsync(videoPath, [1, 2, 3, 4]);
        File.SetLastWriteTimeUtc(videoPath, new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc));
        FileInfo source = new(videoPath);
        TimeSpan duration = TimeSpan.FromSeconds(100);
        const int count = 20;
        string cacheRoot = Path.Combine(temporary.Path, "cache");
        string cacheKey = ThumbnailService.CreateCacheKey(
            Path.GetFullPath(videoPath),
            source.Length,
            source.LastWriteTimeUtc,
            duration,
            count);
        string cacheDirectory = Path.Combine(cacheRoot, cacheKey);
        WriteFrames(cacheDirectory, count, ThumbnailService.ThumbnailWidth, ThumbnailService.ThumbnailHeight);
        ThrowingToolResolver resolver = new();
        ThumbnailService service = new(resolver, cacheRoot);

        IReadOnlyList<ThumbnailFrame> frames = await service.GenerateAsync(
            videoPath,
            duration,
            count,
            CancellationToken.None);

        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(count, frames.Count);
        Assert.Equal(TimeSpan.Zero, frames[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(95), frames[^1].Timestamp);
        Assert.All(frames, frame => Assert.True(Path.IsPathFullyQualified(frame.ImagePath)));
        Assert.Equal("frame-000.jpg", Path.GetFileName(frames[0].ImagePath));
        Assert.Equal("frame-019.jpg", Path.GetFileName(frames[^1].ImagePath));
    }

    [Fact]
    public void TryReadCache_RejectsMissingCorruptAndWrongSizedFrames()
    {
        using TemporaryDirectory temporary = new();
        string cacheDirectory = Path.Combine(temporary.Path, "cache");
        WriteFrames(cacheDirectory, 20, 160, 90);

        Assert.True(ThumbnailService.TryReadCache(
            cacheDirectory,
            TimeSpan.FromMinutes(1),
            20,
            out IReadOnlyList<ThumbnailFrame> valid));
        Assert.Equal(20, valid.Count);

        File.Delete(Path.Combine(cacheDirectory, "frame-019.jpg"));
        Assert.False(ThumbnailService.TryReadCache(
            cacheDirectory,
            TimeSpan.FromMinutes(1),
            20,
            out _));

        WriteJpeg(Path.Combine(cacheDirectory, "frame-019.jpg"), 159, 90);
        Assert.False(ThumbnailService.TryReadCache(
            cacheDirectory,
            TimeSpan.FromMinutes(1),
            20,
            out _));

        WriteJpeg(Path.Combine(cacheDirectory, "frame-019.jpg"), 160, 90);
        File.WriteAllText(Path.Combine(cacheDirectory, "frame-010.jpg"), "not a jpeg");
        Assert.False(ThumbnailService.TryReadCache(
            cacheDirectory,
            TimeSpan.FromMinutes(1),
            20,
            out _));
    }

    [Fact]
    public async Task GenerateAsync_FailedProcessDoesNotPublishPartialCache()
    {
        using TemporaryDirectory temporary = new();
        string videoPath = Path.Combine(temporary.Path, "source.mp4");
        await File.WriteAllBytesAsync(videoPath, [1, 2, 3, 4]);
        string cacheRoot = Path.Combine(temporary.Path, "cache");
        string failingExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "where.exe");
        ThumbnailService service = new(
            new FixedToolResolver(failingExecutable),
            cacheRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GenerateAsync(
            videoPath,
            TimeSpan.FromSeconds(1),
            20,
            CancellationToken.None));

        Assert.Empty(Directory.Exists(cacheRoot)
            ? Directory.GetDirectories(cacheRoot, "*", SearchOption.TopDirectoryOnly)
            : []);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(41)]
    public async Task GenerateAsync_RejectsThumbnailCountOutsideSupportedRange(int count)
    {
        using TemporaryDirectory temporary = new();
        string videoPath = Path.Combine(temporary.Path, "source.mp4");
        await File.WriteAllBytesAsync(videoPath, [1]);
        ThumbnailService service = new(new ThrowingToolResolver(), Path.Combine(temporary.Path, "cache"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GenerateAsync(
            videoPath,
            TimeSpan.FromSeconds(1),
            count,
            CancellationToken.None));
    }

    [Fact]
    public void DefaultCacheRoot_IsUnderCliporaLocalApplicationData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipora",
            "Cache",
            "Thumbnails");

        Assert.Equal(Path.GetFullPath(expected), ThumbnailService.GetDefaultCacheRoot());
    }

    private static void WriteFrames(string directory, int count, int width, int height)
    {
        Directory.CreateDirectory(directory);
        for (int index = 0; index < count; index++)
        {
            WriteJpeg(Path.Combine(directory, $"frame-{index:000}.jpg"), width, height);
        }
    }

    private static void WriteJpeg(string path, int width, int height)
    {
        byte[] bytes =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x0B, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x01, 0x01, 0x11, 0x00,
            0xFF, 0xD9,
        ];
        File.WriteAllBytes(path, bytes);
    }

    private sealed class ThrowingToolResolver : IBundledToolResolver
    {
        public int ResolveCount { get; private set; }

        public BundledToolPaths Resolve()
        {
            ResolveCount++;
            throw new InvalidOperationException("The validated cache should be reused.");
        }
    }

    private sealed class FixedToolResolver(string executablePath) : IBundledToolResolver
    {
        public BundledToolPaths Resolve()
        {
            return new BundledToolPaths(executablePath, executablePath);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Clipora.Media.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
