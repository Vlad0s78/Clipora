using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface ISettingsService
{
    /// <summary>
    /// Path of the copy kept after an unreadable settings file was replaced by defaults, or <c>null</c>.
    /// </summary>
    string? InvalidFileBackupPath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
