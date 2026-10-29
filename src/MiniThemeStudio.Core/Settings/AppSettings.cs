using MiniThemeStudio.Core.Audio;

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
    BgmAudioChannels BgmChannels = BgmAudioChannels.Stereo,
    bool IncludeBgm = true,
    bool AdvancedEditing = false);
