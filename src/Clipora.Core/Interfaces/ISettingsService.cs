using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
