namespace MiniThemeStudio.Core.Settings;

public interface IAppSettingsStore
{
    AppSettings Current { get; }

    void Save(AppSettings settings);
}
