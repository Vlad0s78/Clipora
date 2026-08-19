using Clipora.Core.Models;

namespace Clipora.Core;

/// <summary>
/// Chooses the container of the produced file. Re-encoded video uses HEVC or AV1 with AAC audio,
/// which containers such as AVI or WebM cannot legally hold, so those inputs are written as MP4.
/// </summary>
public static class OutputContainerPolicy
{
    public const string DefaultExtension = ".mp4";

    private static readonly HashSet<string> ReEncodeCapableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".mov",
    };

    public static string ResolveExtension(string sourceExtension, EncodeMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceExtension);

        // Копирование потоков сохраняет исходный контейнер: перекодирования нет.
        if (mode is EncodeMode.TrimOnly)
        {
            return sourceExtension;
        }

        return ReEncodeCapableExtensions.Contains(sourceExtension)
            ? sourceExtension
            : DefaultExtension;
    }
}
