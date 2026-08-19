using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.Media;

public sealed class ThumbnailService : IThumbnailService
{
    internal const int MinimumThumbnailCount = 20;
    internal const int MaximumThumbnailCount = 40;
    internal const int ThumbnailWidth = 160;
    internal const int ThumbnailHeight = 90;
    internal const int MaximumDiagnosticLength = 4096;
    internal const string CacheSchemaVersion = "crop-fill-v2";
    internal const int MaximumCachedVideos = 24;

    private static readonly TimeSpan CancellationDrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly Dictionary<string, CacheLockEntry> CacheLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IBundledToolResolver _toolResolver;
    private readonly string _cacheRoot;

    public ThumbnailService(IBundledToolResolver toolResolver)
        : this(toolResolver, GetDefaultCacheRoot())
    {
    }

    public ThumbnailService(IBundledToolResolver toolResolver, string cacheRoot)
    {
        _toolResolver = toolResolver ?? throw new ArgumentNullException(nameof(toolResolver));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _cacheRoot = Path.GetFullPath(cacheRoot);
    }

    public async Task<IReadOnlyList<ThumbnailFrame>> GenerateAsync(
        string videoPath,
        TimeSpan duration,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(videoPath, duration, count);

        string fullVideoPath = Path.GetFullPath(videoPath);
        FileInfo source = GetSourceInfo(fullVideoPath);
        string cacheKey = CreateCacheKey(
            fullVideoPath,
            source.Length,
            source.LastWriteTimeUtc,
            duration,
            count);
        string cacheDirectory = Path.Combine(_cacheRoot, cacheKey);

        if (TryReadCache(cacheDirectory, duration, count, out IReadOnlyList<ThumbnailFrame> cachedFrames))
        {
            return cachedFrames;
        }

        SemaphoreSlim cacheLock = RentCacheLock(cacheKey);

        try
        {
            await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReturnCacheLock(cacheKey);
            throw;
        }

        try
        {
            if (TryReadCache(cacheDirectory, duration, count, out cachedFrames))
            {
                return cachedFrames;
            }

            Directory.CreateDirectory(_cacheRoot);
            string stagingDirectory = Path.Combine(
                _cacheRoot,
                $"{cacheKey}.staging.{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                string ffmpegPath = _toolResolver.Resolve().FfmpegPath;

                // Сначала берём только ключевые кадры: это на порядок быстрее полного декодирования.
                string diagnostic = await RunFfmpegAsync(
                        CreateStartInfo(ffmpegPath, fullVideoPath, stagingDirectory, duration, count, keyFramesOnly: true),
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureSourceUnchanged(source, fullVideoPath);

                // Если ключевых кадров в файле меньше, чем нужно миниатюр, декодируем полностью.
                if (!TryReadCache(stagingDirectory, duration, count, out _))
                {
                    ClearDirectory(stagingDirectory);
                    diagnostic = await RunFfmpegAsync(
                            CreateStartInfo(ffmpegPath, fullVideoPath, stagingDirectory, duration, count, keyFramesOnly: false),
                            cancellationToken)
                        .ConfigureAwait(false);
                    EnsureSourceUnchanged(source, fullVideoPath);

                    if (!TryReadCache(stagingDirectory, duration, count, out _))
                    {
                        throw new InvalidDataException(
                            $"FFmpeg did not create {count} valid {ThumbnailWidth}x{ThumbnailHeight} thumbnails."
                            + FormatDiagnostic(diagnostic));
                    }
                }

                PublishCache(stagingDirectory, cacheDirectory, duration, count);
                TrimCache(_cacheRoot);
                if (!TryReadCache(cacheDirectory, duration, count, out IReadOnlyList<ThumbnailFrame> frames))
                {
                    throw new InvalidDataException("The published thumbnail cache is invalid.");
                }

                return frames;
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
        finally
        {
            cacheLock.Release();
            ReturnCacheLock(cacheKey);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string ffmpegPath,
        string videoPath,
        string stagingDirectory,
        TimeSpan duration,
        int count,
        bool keyFramesOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        string durationSeconds = duration.TotalSeconds.ToString("0.#########", CultureInfo.InvariantCulture);
        string filter = string.Concat(
            "setpts=PTS-STARTPTS,",
            "fps=fps=", count.ToString(CultureInfo.InvariantCulture), "/", durationSeconds, ":round=up,",
            "scale=w=", ThumbnailWidth.ToString(CultureInfo.InvariantCulture),
            ":h=", ThumbnailHeight.ToString(CultureInfo.InvariantCulture),
            ":force_original_aspect_ratio=increase,",
            "crop=", ThumbnailWidth.ToString(CultureInfo.InvariantCulture),
            ":", ThumbnailHeight.ToString(CultureInfo.InvariantCulture),
            ",setsar=1");
        string outputPattern = Path.Combine(stagingDirectory, "frame-%03d.jpg");

        ProcessStartInfo startInfo = new()
        {
            FileName = Path.GetFullPath(ffmpegPath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(ffmpegPath)) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };

        AddArguments(
            startInfo,
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-y");

        if (keyFramesOnly)
        {
            AddArguments(startInfo, "-skip_frame", "nokey");
        }

        AddArguments(
            startInfo,
            "-i", Path.GetFullPath(videoPath),
            "-map", "0:V:0",
            "-an",
            "-sn",
            "-dn",
            "-vf", filter,
            "-frames:v", count.ToString(CultureInfo.InvariantCulture),
            "-q:v", "3",
            "-start_number", "0",
            outputPattern);

        return startInfo;
    }

    internal static string CreateCacheKey(
        string fullVideoPath,
        long fileLength,
        DateTime lastWriteTimeUtc,
        TimeSpan duration,
        int count)
    {
        string normalizedPath = Path.GetFullPath(fullVideoPath).ToUpperInvariant();
        string value = string.Join(
            "\n",
            CacheSchemaVersion,
            normalizedPath,
            fileLength.ToString(CultureInfo.InvariantCulture),
            lastWriteTimeUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            duration.Ticks.ToString(CultureInfo.InvariantCulture),
            count.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static bool TryReadCache(
        string cacheDirectory,
        TimeSpan duration,
        int count,
        out IReadOnlyList<ThumbnailFrame> frames)
    {
        frames = Array.Empty<ThumbnailFrame>();
        if (!Directory.Exists(cacheDirectory))
        {
            return false;
        }

        try
        {
            string[] files = Directory.GetFiles(cacheDirectory, "frame-*.jpg", SearchOption.TopDirectoryOnly);
            if (files.Length != count)
            {
                return false;
            }

            List<ThumbnailFrame> result = new(count);
            for (int index = 0; index < count; index++)
            {
                string imagePath = Path.GetFullPath(
                    Path.Combine(cacheDirectory, $"frame-{index:000}.jpg"));
                if (!IsValidThumbnail(imagePath))
                {
                    return false;
                }

                result.Add(new ThumbnailFrame(CalculateTimestamp(duration, count, index), imagePath));
            }

            frames = result;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    internal static TimeSpan CalculateTimestamp(TimeSpan duration, int count, int index)
    {
        long ticks = ((duration.Ticks / count) * index) + (((duration.Ticks % count) * index) / count);
        return TimeSpan.FromTicks(ticks);
    }

    internal static string GetDefaultCacheRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }

        return Path.Combine(localAppData, "Clipora", "Cache", "Thumbnails");
    }

    private static void ValidateRequest(string videoPath, TimeSpan duration, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (count is < MinimumThumbnailCount or > MaximumThumbnailCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"Thumbnail count must be between {MinimumThumbnailCount} and {MaximumThumbnailCount}.");
        }
    }

    private static FileInfo GetSourceInfo(string videoPath)
    {
        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("The input video file was not found.", videoPath);
        }

        FileInfo source = new(videoPath);
        source.Refresh();
        return source;
    }

    private static void EnsureSourceUnchanged(FileInfo source, string videoPath)
    {
        FileInfo current = GetSourceInfo(videoPath);
        if (current.Length != source.Length || current.LastWriteTimeUtc != source.LastWriteTimeUtc)
        {
            throw new IOException("The source video changed while thumbnails were being generated.");
        }
    }

    private static async Task<string> RunFfmpegAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start FFmpeg at '{startInfo.FileName}'.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Unable to start FFmpeg at '{startInfo.FileName}'.",
                exception);
        }

        Task<string> diagnosticTask = ReadBoundedDiagnosticAsync(process.StandardError);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await DrainAfterCancellationAsync(process, diagnosticTask).ConfigureAwait(false);
            throw;
        }

        string diagnostic = await diagnosticTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"FFmpeg thumbnail generation failed with exit code {process.ExitCode}."
                + FormatDiagnostic(diagnostic));
        }

        return diagnostic;
    }

    private static async Task<string> ReadBoundedDiagnosticAsync(StreamReader reader)
    {
        BoundedTextBuffer buffer = new(MaximumDiagnosticLength);
        char[] rented = ArrayPool<char>.Shared.Rent(1024);

        try
        {
            int read;
            while ((read = await reader.ReadAsync(rented.AsMemory()).ConfigureAwait(false)) > 0)
            {
                buffer.Append(rented.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }

        return buffer.ToString();
    }

    private static async Task DrainAfterCancellationAsync(Process process, Task<string> diagnosticTask)
    {
        Task drainTask = Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), diagnosticTask);
        try
        {
            await drainTask.WaitAsync(CancellationDrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = drainTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
        }
    }

    private static void PublishCache(
        string stagingDirectory,
        string cacheDirectory,
        TimeSpan duration,
        int count)
    {
        if (Directory.Exists(cacheDirectory))
        {
            if (TryReadCache(cacheDirectory, duration, count, out _))
            {
                return;
            }

            TryDeleteDirectory(cacheDirectory);
            if (Directory.Exists(cacheDirectory))
            {
                throw new IOException($"Unable to replace invalid thumbnail cache '{cacheDirectory}'.");
            }
        }

        try
        {
            Directory.Move(stagingDirectory, cacheDirectory);
        }
        catch (IOException) when (TryReadCache(cacheDirectory, duration, count, out _))
        {
        }
    }

    private static SemaphoreSlim RentCacheLock(string cacheKey)
    {
        lock (CacheLocks)
        {
            if (CacheLocks.TryGetValue(cacheKey, out CacheLockEntry? entry))
            {
                entry.ReferenceCount++;
                return entry.Semaphore;
            }

            CacheLockEntry newEntry = new();
            CacheLocks.Add(cacheKey, newEntry);
            return newEntry.Semaphore;
        }
    }

    private static void ReturnCacheLock(string cacheKey)
    {
        lock (CacheLocks)
        {
            if (!CacheLocks.TryGetValue(cacheKey, out CacheLockEntry? entry))
            {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0)
            {
                return;
            }

            CacheLocks.Remove(cacheKey);
            entry.Semaphore.Dispose();
        }
    }

    private static void TrimCache(string cacheRoot)
    {
        try
        {
            DirectoryInfo[] directories = new DirectoryInfo(cacheRoot).GetDirectories();
            if (directories.Length <= MaximumCachedVideos)
            {
                return;
            }

            IEnumerable<DirectoryInfo> obsoleteDirectories = directories
                .Where(static directory => !directory.Name.Contains(".staging.", StringComparison.Ordinal))
                .OrderByDescending(static directory => directory.LastWriteTimeUtc)
                .Skip(MaximumCachedVideos);

            foreach (DirectoryInfo directory in obsoleteDirectories)
            {
                TryDeleteDirectory(directory.FullName);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static bool IsValidThumbnail(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 16 || stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
            {
                return false;
            }

            stream.Seek(-2, SeekOrigin.End);
            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD9)
            {
                return false;
            }

            stream.Position = 2;
            while (stream.Position + 4 <= stream.Length)
            {
                int prefix = stream.ReadByte();
                if (prefix != 0xFF)
                {
                    continue;
                }

                int marker;
                do
                {
                    marker = stream.ReadByte();
                }
                while (marker == 0xFF);

                if (marker is -1 or 0xD9 or 0xDA)
                {
                    return false;
                }

                int segmentLength = ReadBigEndianUInt16(stream);
                if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
                {
                    return false;
                }

                if (IsStartOfFrame(marker))
                {
                    if (segmentLength < 7)
                    {
                        return false;
                    }

                    _ = stream.ReadByte();
                    int height = ReadBigEndianUInt16(stream);
                    int width = ReadBigEndianUInt16(stream);
                    return width == ThumbnailWidth && height == ThumbnailHeight;
                }

                stream.Seek(segmentLength - 2, SeekOrigin.Current);
            }

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsStartOfFrame(int marker)
    {
        return marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
    }

    private static int ReadBigEndianUInt16(Stream stream)
    {
        int high = stream.ReadByte();
        int low = stream.ReadByte();
        return high < 0 || low < 0 ? -1 : (high << 8) | low;
    }

    private static void ClearDirectory(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static string FormatDiagnostic(string diagnostic)
    {
        string value = diagnostic.Trim();
        return value.Length == 0 ? string.Empty : $" FFmpeg output: {value}";
    }

    private sealed class CacheLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; } = 1;
    }

    private sealed class BoundedTextBuffer
    {
        private readonly int _maximumLength;
        private readonly StringBuilder _value = new();
        private bool _wasTruncated;

        public BoundedTextBuffer(int maximumLength)
        {
            _maximumLength = maximumLength;
        }

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.Length >= _maximumLength)
            {
                _value.Clear();
                _value.Append(value[^_maximumLength..]);
                _wasTruncated = true;
                return;
            }

            _value.Append(value);
            int excessLength = _value.Length - _maximumLength;
            if (excessLength > 0)
            {
                _value.Remove(0, excessLength);
                _wasTruncated = true;
            }
        }

        public override string ToString()
        {
            string result = _value.ToString().Trim();
            if (!_wasTruncated || result.Length == 0)
            {
                return result;
            }

            if (result.Length >= _maximumLength)
            {
                return string.Concat("…", result.AsSpan(result.Length - _maximumLength + 1));
            }

            return $"…{result}";
        }
    }
}
