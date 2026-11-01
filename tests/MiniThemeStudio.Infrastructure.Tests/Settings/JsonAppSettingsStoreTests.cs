using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Infrastructure.Settings;

namespace MiniThemeStudio.Infrastructure.Tests.Settings;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public void UsesUsbConnectionDefaultsWhenSettingsDoNotExist()
    {
        string rootPath = Directory.CreateTempSubdirectory().FullName;

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
        string rootPath = Directory.CreateTempSubdirectory().FullName;
        string settingsPath = Path.Combine(rootPath, "settings.json");
        var store = new JsonAppSettingsStore(settingsPath);
        AppSettings settings = store.Current with
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

    [Fact]
    public void SavesAndLoadsFFmpegAndBgmPreferences()
    {
        string rootPath = Directory.CreateTempSubdirectory().FullName;
        string settingsPath = Path.Combine(rootPath, "settings.json");
        var store = new JsonAppSettingsStore(settingsPath);
        AppSettings settings = store.Current with
        {
            FFmpegPath = "C:\\Tools\\ffmpeg.exe",
            ConvertBgmAudio = true,
            BgmChannels = MiniThemeStudio.Core.Audio.BgmAudioChannels.Mono,
            IncludeBgm = false,
        };

        try
        {
            store.Save(settings);
            var reloadedStore = new JsonAppSettingsStore(settingsPath);

            Assert.Equal("C:\\Tools\\ffmpeg.exe", reloadedStore.Current.FFmpegPath);
            Assert.True(reloadedStore.Current.ConvertBgmAudio);
            Assert.Equal(MiniThemeStudio.Core.Audio.BgmAudioChannels.Mono, reloadedStore.Current.BgmChannels);
            Assert.False(reloadedStore.Current.IncludeBgm);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
