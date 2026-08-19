using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.Core.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters =
        {
            new TrimShortcutBindingsJsonConverter(),
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false),
        },
    };

    private const string InvalidFileSuffix = ".invalid.json";

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _invalidFileBackupPath;

    public JsonSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipora",
            "settings.json"))
    {
    }

    public JsonSettingsService(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public string? InvalidFileBackupPath => Volatile.Read(ref _invalidFileBackupPath);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using FileStream stream = new(
                _settingsPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });

            AppSettings? settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (settings is null || !IsSupported(settings))
            {
                return new AppSettings();
            }

            return settings with
            {
                TrimShortcuts = TrimShortcutPolicy.NormalizeOrDefault(settings.TrimShortcuts),
                WindowLayout = NormalizeWindowLayout(settings.WindowLayout),
            };
        }
        catch (FileNotFoundException)
        {
            return new AppSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            PreserveInvalidFile();
            return new AppSettings();
        }
        catch (NotSupportedException)
        {
            PreserveInvalidFile();
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!TrimShortcutPolicy.TryNormalize(
                settings.TrimShortcuts,
                out TrimShortcutBindings normalizedShortcuts,
                out _) ||
            !IsSupported(settings) ||
            settings.OutputMode is OutputMode.CustomFolder &&
            !OutputDirectoryPolicy.TryNormalizeExistingDirectory(settings.CustomOutputFolder, out _))
        {
            throw new ArgumentException("Settings contain unsupported values.", nameof(settings));
        }

        AppSettings normalizedSettings = settings with
        {
            TrimShortcuts = normalizedShortcuts,
            WindowLayout = NormalizeWindowLayout(settings.WindowLayout),
        };

        cancellationToken.ThrowIfCancellationRequested();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            string directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("The settings path has no parent directory.");
            Directory.CreateDirectory(directory);

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                    }))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        normalizedSettings,
                        SerializerOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                CommitTemporaryFile(temporaryPath, _settingsPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // Нечитаемый файл сохраняется рядом: настройки пользователя не должны исчезать молча.
    private void PreserveInvalidFile()
    {
        string backupPath = string.Concat(_settingsPath, InvalidFileSuffix);

        try
        {
            File.Move(_settingsPath, backupPath, overwrite: true);
            Volatile.Write(ref _invalidFileBackupPath, backupPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static WindowLayout? NormalizeWindowLayout(WindowLayout? layout)
    {
        return layout is { IsValid: true } ? layout : null;
    }

    private static bool IsSupported(AppSettings settings)
    {
        return Enum.IsDefined(settings.OutputMode)
            && settings.Language is null or "ru-RU" or "en-US";
    }

    private static void CommitTemporaryFile(string temporaryPath, string settingsPath)
    {
        if (File.Exists(settingsPath))
        {
            File.Replace(temporaryPath, settingsPath, destinationBackupFileName: null);
            return;
        }

        try
        {
            File.Move(temporaryPath, settingsPath);
        }
        catch (IOException) when (File.Exists(settingsPath))
        {
            File.Replace(temporaryPath, settingsPath, destinationBackupFileName: null);
        }
    }
}
