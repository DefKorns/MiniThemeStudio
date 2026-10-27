namespace MiniThemeStudio.Core.Audio;

public sealed record FFmpegStatus(
    bool IsAvailable,
    string? Version,
    string? ErrorMessage = null)
{
    public static FFmpegStatus NotConfigured => new(false, null, "FFmpeg path is not configured.");
    public static FFmpegStatus NotFound(string path) => new(false, null, $"FFmpeg executable not found at '{path}'.");
    public static FFmpegStatus Available(string version) => new(true, version);
}
