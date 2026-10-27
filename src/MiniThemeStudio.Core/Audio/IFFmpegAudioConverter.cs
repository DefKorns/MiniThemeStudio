namespace MiniThemeStudio.Core.Audio;

public interface IFFmpegAudioConverter
{
    Task<FFmpegStatus> GetStatusAsync(string? ffmpegPath, CancellationToken cancellationToken = default);

    Task<AudioConversionResult> ConvertToConsoleWavAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        BgmAudioChannels channels = BgmAudioChannels.Stereo,
        CancellationToken cancellationToken = default);
}
