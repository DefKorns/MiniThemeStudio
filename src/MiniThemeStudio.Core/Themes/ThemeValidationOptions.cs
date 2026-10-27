using MiniThemeStudio.Core.Audio;

namespace MiniThemeStudio.Core.Themes;

public sealed record ThemeValidationOptions(
    bool ConvertBgmAudio = false,
    string? FFmpegPath = null,
    BgmAudioChannels BgmChannels = BgmAudioChannels.Stereo,
    bool IncludeBgm = true);
