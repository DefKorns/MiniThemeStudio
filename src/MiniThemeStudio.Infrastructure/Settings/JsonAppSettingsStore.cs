using System.Text.Json;
using System.Text.Json.Serialization;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.Infrastructure.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;

    public JsonAppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniThemeStudio",
            "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        var directoryPath = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The settings file must have a parent directory.");
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";

        Directory.CreateDirectory(directoryPath);
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, _settingsPath, true);
            Current = settings;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateDefaults();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsPath),
                SerializerOptions);

            if (settings is null)
            {
                return CreateDefaults();
            }

            Validate(settings);
            return settings;
        }
        catch (JsonException)
        {
            return CreateDefaults();
        }
        catch (ArgumentException)
        {
            return CreateDefaults();
        }
    }

    private static AppSettings CreateDefaults()
    {
        var applicationDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniThemeStudio");

        return new AppSettings(
            Path.Combine(applicationDataPath, "base-ui"),
            Path.Combine(Path.GetTempPath(), "MiniThemeStudio", "drafts"),
            "",
            ApplicationThemePreference.System,
            "169.254.13.37");
    }

    private static void Validate(AppSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.BaseUiRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DraftRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ConsoleHost);
    }
}
