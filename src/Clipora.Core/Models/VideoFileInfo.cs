namespace Clipora.Core.Models;

public sealed record VideoFileInfo(
    string Path,
    TimeSpan Duration,
    long FileSizeBytes,
    long? BitrateBitsPerSecond,
    IReadOnlyList<VideoStreamInfo> VideoStreams,
    IReadOnlyList<AudioStreamInfo> AudioStreams,
    IReadOnlyList<SubtitleStreamInfo> SubtitleStreams,
    IReadOnlyDictionary<string, string> Metadata,
    int? RotationDegrees)
{
    public VideoStreamInfo? PrimaryVideoStream =>
        VideoStreams.FirstOrDefault(static stream => stream.IsDefault && !stream.IsAttachedPicture) ??
        VideoStreams.FirstOrDefault(static stream => !stream.IsAttachedPicture);

    public AudioStreamInfo? PrimaryAudioStream =>
        AudioStreams.FirstOrDefault(static stream => stream.IsDefault) ??
        AudioStreams.FirstOrDefault();
}
