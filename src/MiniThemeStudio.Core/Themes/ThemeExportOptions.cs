using MiniThemeStudio.Core.Audio;

namespace MiniThemeStudio.Core.Themes;

public sealed record ThemeExportOptions(
    bool ConvertBgmAudio = false,
    string? FFmpegPath = null,
    BgmAudioChannels BgmChannels = BgmAudioChannels.Stereo,
    bool IncludeBgm = true);
