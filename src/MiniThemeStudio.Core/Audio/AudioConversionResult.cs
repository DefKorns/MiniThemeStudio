namespace MiniThemeStudio.Core.Audio;

public sealed record AudioConversionResult(
    bool Success,
    string OutputPath,
    string? ErrorMessage = null,
    WavAudioInfo? ConvertedInfo = null)
{
    public static AudioConversionResult Succeeded(string outputPath, WavAudioInfo info) =>
        new(true, outputPath, null, info);

    public static AudioConversionResult Failed(string outputPath, string errorMessage) =>
        new(false, outputPath, errorMessage, null);
}
