using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IEncoderDetector
{
    Task<IReadOnlyList<EncoderCapability>> DetectAsync(CancellationToken cancellationToken);
}
