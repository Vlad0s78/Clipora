using Clipora.Shell;

namespace Clipora.Shell.Tests;

public sealed class ExplorerIntegrationServiceTests
{
    private const string ExecutablePath = @"C:\Program Files\Клипора 100%\Clipora.exe";
    private static readonly string[] Extensions = [".mp4", ".mkv", ".mov"];

    [Fact]
    public void SetEnabled_RegistersExactOwnedKeysAndQuotedCommands()
    {
        FakeCurrentUserRegistry registry = new();
        ExplorerIntegrationService service = new(registry);

        service.SetEnabled(true, ExecutablePath, "ru-RU");

        foreach (string extension in Extensions)
        {
            string openKey = OwnedKey(extension, "Clipora.Open");
            string compressKey = OwnedKey(extension, "Clipora.Compress");
            Assert.Equal("Открыть в Clipora", registry.GetString(openKey, null));
            Assert.Equal("Сжать с помощью Clipora", registry.GetString(compressKey, null));
            Assert.Equal($"\"{ExecutablePath}\" \"%1\"", registry.GetString($"{openKey}\\command", null));
            Assert.Equal(
                $"\"{ExecutablePath}\" --compress \"%1\"",
                registry.GetString($"{compressKey}\\command", null));
            Assert.Equal($"\"{ExecutablePath}\",0", registry.GetString(openKey, "Icon"));
            Assert.Equal("Single", registry.GetString(compressKey, "MultiSelectModel"));
        }

        Assert.Equal(24, registry.Values.Count);
        Assert.True(service.IsEnabled(ExecutablePath));
    }

    [Fact]
    public void SetEnabled_EnableTwice_IsIdempotentAndUpdatesLocalizedLabels()
    {
        FakeCurrentUserRegistry registry = new();
        ExplorerIntegrationService service = new(registry);

        service.SetEnabled(true, ExecutablePath, "ru-RU");
        service.SetEnabled(true, ExecutablePath, "en-US");

        Assert.Equal(24, registry.Values.Count);
        Assert.Equal("Open in Clipora", registry.GetString(OwnedKey(".mp4", "Clipora.Open"), null));
        Assert.Equal("Compress with Clipora", registry.GetString(OwnedKey(".mp4", "Clipora.Compress"), null));
        Assert.True(service.IsEnabled(ExecutablePath));
    }

    [Fact]
    public void SetEnabled_DisableTwice_DeletesOnlyOwnedCliporaKeys()
    {
        FakeCurrentUserRegistry registry = new();
        ExplorerIntegrationService service = new(registry);
        string foreignKey = @"Software\Classes\SystemFileAssociations\.mp4\shell\AnotherApp";
        registry.SetString(foreignKey, null, "Keep me");
        service.SetEnabled(true, ExecutablePath, "en-US");

        service.SetEnabled(false, ExecutablePath, "en-US");
        service.SetEnabled(false, ExecutablePath, "en-US");

        Assert.Equal("Keep me", registry.GetString(foreignKey, null));
        Assert.All(
            registry.DeleteCalls,
            key => Assert.Matches(
                @"^Software\\Classes\\SystemFileAssociations\\\.(mp4|mkv|mov)\\shell\\Clipora\.(Open|Compress)$",
                key));
        Assert.Equal(12, registry.DeleteCalls.Count);
        Assert.False(service.IsEnabled(ExecutablePath));
    }

    [Fact]
    public void SetEnabled_PartialRegistrationFailure_RemovesAllOwnedKeys()
    {
        FakeCurrentUserRegistry registry = new() { ThrowAfterSetCount = 5 };
        ExplorerIntegrationService service = new(registry);

        Assert.Throws<IOException>(() => service.SetEnabled(true, ExecutablePath, "en-US"));

        Assert.DoesNotContain(
            registry.Values.Keys,
            key => key.SubKeyPath.Contains(@"\shell\Clipora.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsEnabled_WhenACommandWasChanged_ReturnsFalse()
    {
        FakeCurrentUserRegistry registry = new();
        ExplorerIntegrationService service = new(registry);
        service.SetEnabled(true, ExecutablePath, "en-US");
        registry.SetString(
            $"{OwnedKey(".mov", "Clipora.Compress")}\\command",
            null,
            "changed");

        Assert.False(service.IsEnabled(ExecutablePath));
    }

    [Fact]
    public void SetEnabled_NonExecutablePath_IsRejectedWithoutRegistryWrites()
    {
        FakeCurrentUserRegistry registry = new();
        ExplorerIntegrationService service = new(registry);

        Assert.Throws<ArgumentException>(() => service.SetEnabled(true, @"C:\Clipora\Clipora.dll", "en-US"));
        Assert.Empty(registry.Values);
        Assert.Empty(registry.DeleteCalls);
    }

    private static string OwnedKey(string extension, string command)
    {
        return $"Software\\Classes\\SystemFileAssociations\\{extension}\\shell\\{command}";
    }

    private sealed class FakeCurrentUserRegistry : ICurrentUserRegistry
    {
        private int _setCount;

        public Dictionary<(string SubKeyPath, string ValueName), string> Values { get; } = new();

        public List<string> DeleteCalls { get; } = [];

        public int? ThrowAfterSetCount { get; init; }

        public string? GetString(string subKeyPath, string? valueName)
        {
            return Values.GetValueOrDefault((subKeyPath, valueName ?? string.Empty));
        }

        public void SetString(string subKeyPath, string? valueName, string value)
        {
            _setCount++;
            if (ThrowAfterSetCount is int throwAfter && _setCount > throwAfter)
            {
                throw new IOException("Injected registry failure.");
            }

            Values[(subKeyPath, valueName ?? string.Empty)] = value;
        }

        public void DeleteSubKeyTree(string subKeyPath)
        {
            DeleteCalls.Add(subKeyPath);
            foreach ((string SubKeyPath, string ValueName) key in Values.Keys
                         .Where(key => key.SubKeyPath.Equals(subKeyPath, StringComparison.OrdinalIgnoreCase) ||
                                       key.SubKeyPath.StartsWith($"{subKeyPath}\\", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                Values.Remove(key);
            }
        }
    }
}
