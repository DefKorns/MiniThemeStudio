using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Infrastructure.Settings;

namespace MiniThemeStudio.Infrastructure.Tests.Settings;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public void UsesUsbConnectionDefaultsWhenSettingsDoNotExist()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(rootPath, "settings.json"));

            Assert.Equal("169.254.13.37", store.Current.ConsoleHost);
            Assert.False(store.Current.InitialSetupCompleted);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void SavesAndLoadsNonSecretSettings()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var settingsPath = Path.Combine(rootPath, "settings.json");
        var store = new JsonAppSettingsStore(settingsPath);
        var settings = store.Current with
        {
            ConsoleHost = "192.168.3.197",
            InitialSetupCompleted = true,
        };

        try
        {
            store.Save(settings);
            var reloadedStore = new JsonAppSettingsStore(settingsPath);

            Assert.Equal(settings, reloadedStore.Current);
            Assert.DoesNotContain("password", File.ReadAllText(settingsPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

}
