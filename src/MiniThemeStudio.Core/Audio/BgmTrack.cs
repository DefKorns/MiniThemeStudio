namespace MiniThemeStudio.Core.Audio;

public sealed record BgmTrack(
    string Name,
    string DisplayName,
    string RelativePath,
    string? DraftFilePath,
    string? BaseFilePath,
    bool ExistsInDraft,
    bool ExistsInBase,
    WavAudioInfo? AudioInfo,
    string StatusDescription,
    bool CanMute = false,
    bool IsMuted = false);
