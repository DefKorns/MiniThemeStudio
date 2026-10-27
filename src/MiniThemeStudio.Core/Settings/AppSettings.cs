namespace MiniThemeStudio.Core.Settings;

public sealed record AppSettings(
    string DraftRootPath,
    string DefaultThemeDestinationPath,
    ApplicationThemePreference Theme,
    string ConsoleHost,
    bool InitialSetupCompleted = false,
    ConsoleConnectionMode ConsoleConnectionMode = ConsoleConnectionMode.Usb,
    string FillFontPath = "",
    string FFmpegPath = "",
    bool ConvertBgmAudio = false,
    MiniThemeStudio.Core.Audio.BgmAudioChannels BgmChannels = MiniThemeStudio.Core.Audio.BgmAudioChannels.Stereo,
    bool IncludeBgm = true);
