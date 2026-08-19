using System.Text;
using Clipora.Core.Models;
using Clipora.Core.Services;

namespace Clipora.Core.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Clipora.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenFileDoesNotExist()
    {
        JsonSettingsService service = CreateService();

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsStringEnumAsUtf8WithoutBom()
    {
        JsonSettingsService service = CreateService();
        string customOutputFolder = Path.Combine(_temporaryDirectory, "Видео Clipora");
        Directory.CreateDirectory(customOutputFolder);
        AppSettings expected = new()
        {
            Language = "ru-RU",
            OutputMode = OutputMode.CustomFolder,
            CustomOutputFolder = customOutputFolder,
            ExplorerIntegration = true,
            ShowTechnicalLog = true,
            TrimShortcuts = new TrimShortcutBindings
            {
                PlayPause = new ShortcutGesture(ShortcutKey.Space, ShortcutModifiers.Control),
                SetStart = new ShortcutGesture(ShortcutKey.I, ShortcutModifiers.Shift),
                SetEnd = new ShortcutGesture(ShortcutKey.O, ShortcutModifiers.Alt),
            },
        };

        await service.SaveAsync(expected, CancellationToken.None);

        byte[] bytes = await File.ReadAllBytesAsync(GetSettingsPath());
        string json = Encoding.UTF8.GetString(bytes);
        Assert.False(bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF);
        Assert.Contains("\"outputMode\": \"CustomFolder\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trimShortcuts\"", json, StringComparison.Ordinal);
        Assert.Contains("\"key\": \"Space\"", json, StringComparison.Ordinal);
        Assert.Contains("\"modifiers\": \"Control\"", json, StringComparison.Ordinal);
        Assert.Contains("Видео Clipora", json, StringComparison.Ordinal);
        Assert.Equal(expected, await service.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_MigratesMissingShortcutBlockToDefaults()
    {
        const string json = """
            {
              "language": "ru-RU",
              "outputMode": "AskEveryTime",
              "showTechnicalLog": true
            }
            """;
        await WriteSettingsJsonAsync(json);

        AppSettings settings = await CreateService().LoadAsync(CancellationToken.None);

        Assert.Equal("ru-RU", settings.Language);
        Assert.Equal(OutputMode.AskEveryTime, settings.OutputMode);
        Assert.True(settings.ShowTechnicalLog);
        Assert.Equal(TrimShortcutBindings.Default, settings.TrimShortcuts);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""
        {
          "playPause": { "key": "I", "modifiers": "None" },
          "setStart": { "key": "I", "modifiers": "None" },
          "setEnd": { "key": "O", "modifiers": "None" }
        }
        """)]
    [InlineData("""
        {
          "playPause": { "key": "Space", "modifiers": "Windows" },
          "setStart": { "key": "I", "modifiers": "None" },
          "setEnd": { "key": "O", "modifiers": "None" }
        }
        """)]
    [InlineData("""
        {
          "playPause": { "key": "FutureKey", "modifiers": "None" },
          "setStart": { "key": "I", "modifiers": "None" },
          "setEnd": { "key": "O", "modifiers": "None" }
        }
        """)]
    [InlineData("""
        {
          "playPause": { "key": "Space", "modifiers": "FutureModifier" },
          "setStart": { "key": "I", "modifiers": "None" },
          "setEnd": { "key": "O", "modifiers": "None" }
        }
        """)]
    [InlineData("\"not-an-object\"")]
    public async Task LoadAsync_ReplacesInvalidShortcutBlockWithoutResettingOtherSettings(
        string shortcutJson)
    {
        string json = $$"""
            {
              "language": "en-US",
              "outputMode": "AskEveryTime",
              "showTechnicalLog": true,
              "trimShortcuts": {{shortcutJson}}
            }
            """;
        await WriteSettingsJsonAsync(json);

        AppSettings settings = await CreateService().LoadAsync(CancellationToken.None);

        Assert.Equal("en-US", settings.Language);
        Assert.Equal(OutputMode.AskEveryTime, settings.OutputMode);
        Assert.True(settings.ShowTechnicalLog);
        Assert.Equal(TrimShortcutBindings.Default, settings.TrimShortcuts);
    }

    [Fact]
    public async Task LoadAsync_MigratesPartialShortcutBlockUsingPropertyDefaults()
    {
        const string json = """
            {
              "trimShortcuts": {
                "playPause": { "key": "P", "modifiers": "Control" }
              }
            }
            """;
        await WriteSettingsJsonAsync(json);

        AppSettings settings = await CreateService().LoadAsync(CancellationToken.None);

        Assert.Equal(
            TrimShortcutBindings.Default with
            {
                PlayPause = new ShortcutGesture(ShortcutKey.P, ShortcutModifiers.Control),
            },
            settings.TrimShortcuts);
    }

    [Theory]
    [InlineData("{ malformed")]
    [InlineData("[]")]
    [InlineData("{\"outputMode\":\"FutureMode\"}")]
    [InlineData("{\"outputMode\":1}")]
    [InlineData("{\"language\":\"de-DE\"}")]
    [InlineData("{\"futureOption\":true}")]
    public async Task LoadAsync_ReturnsDefaults_WhenJsonIsBrokenOrUnsupported(string json)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(GetSettingsPath(), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        JsonSettingsService service = CreateService();

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public async Task SaveAsync_AtomicallyReplacesExistingFileAndRemovesTemporaryFile()
    {
        JsonSettingsService service = CreateService();
        await service.SaveAsync(
            new AppSettings { Language = "en-US" },
            CancellationToken.None);

        AppSettings expected = new()
        {
            Language = "ru-RU",
            OutputMode = OutputMode.AskEveryTime,
            ShowTechnicalLog = true,
        };
        await service.SaveAsync(expected, CancellationToken.None);

        Assert.Equal(expected, await service.LoadAsync(CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, ".settings.json.*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_HonorsPreCanceledToken()
    {
        JsonSettingsService service = CreateService();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LoadAsync(cancellation.Token));
    }

    [Fact]
    public async Task SaveAsync_HonorsPreCanceledTokenWithoutCreatingFile()
    {
        JsonSettingsService service = CreateService();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SaveAsync(new AppSettings(), cancellation.Token));
        Assert.False(File.Exists(GetSettingsPath()));
    }

    [Fact]
    public async Task SaveAsync_RejectsMissingCustomOutputFolder()
    {
        JsonSettingsService service = CreateService();
        AppSettings settings = new()
        {
            OutputMode = OutputMode.CustomFolder,
            CustomOutputFolder = Path.Combine(_temporaryDirectory, "missing"),
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(settings, CancellationToken.None));
        Assert.False(File.Exists(GetSettingsPath()));
    }

    [Fact]
    public async Task SaveAsync_RejectsInvalidShortcuts()
    {
        JsonSettingsService service = CreateService();
        AppSettings[] invalidSettings =
        [
            new AppSettings { TrimShortcuts = null! },
            new AppSettings
            {
                TrimShortcuts = TrimShortcutBindings.Default with
                {
                    SetStart = TrimShortcutBindings.Default.PlayPause,
                },
            },
            new AppSettings
            {
                TrimShortcuts = TrimShortcutBindings.Default with
                {
                    PlayPause = new ShortcutGesture(ShortcutKey.Space, ShortcutModifiers.Windows),
                },
            },
        ];

        foreach (AppSettings settings in invalidSettings)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.SaveAsync(settings, CancellationToken.None));
        }

        Assert.False(File.Exists(GetSettingsPath()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsWindowLayout()
    {
        JsonSettingsService service = CreateService();
        AppSettings expected = new()
        {
            WindowLayout = new WindowLayout(1440, 900, IsMaximized: true),
        };

        await service.SaveAsync(expected, CancellationToken.None);

        string json = await File.ReadAllTextAsync(GetSettingsPath());
        Assert.DoesNotContain("isValid", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, await service.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_DropsUndersizedWindowLayoutAndKeepsOtherSettings()
    {
        await WriteSettingsJsonAsync(
            """
            {
              "language": "en-US",
              "windowLayout": { "widthDip": 320, "heightDip": 240, "isMaximized": false }
            }
            """);
        JsonSettingsService service = CreateService();

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.Null(settings.WindowLayout);
        Assert.Equal("en-US", settings.Language);
    }

    [Fact]
    public async Task LoadAsync_KeepsUnreadableFileAsBackupAndReportsIt()
    {
        await WriteSettingsJsonAsync("{ \"language\": \"ru-RU\", ");
        JsonSettingsService service = CreateService();

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        string backupPath = GetSettingsPath() + ".invalid.json";
        Assert.Equal(new AppSettings(), settings);
        Assert.Equal(backupPath, service.InvalidFileBackupPath);
        Assert.True(File.Exists(backupPath));
        Assert.False(File.Exists(GetSettingsPath()));
        Assert.Contains("ru-RU", await File.ReadAllTextAsync(backupPath), StringComparison.Ordinal);
    }

    private JsonSettingsService CreateService()
    {
        return new JsonSettingsService(GetSettingsPath());
    }

    private string GetSettingsPath()
    {
        return Path.Combine(_temporaryDirectory, "settings.json");
    }

    private async Task WriteSettingsJsonAsync(string json)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(
            GetSettingsPath(),
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
