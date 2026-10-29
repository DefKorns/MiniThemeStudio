using System.Text.RegularExpressions;
using MiniThemeStudio.Core.Audio;

namespace MiniThemeStudio.Infrastructure.Audio;

public sealed class FFmpegAudioConverter : IFFmpegAudioConverter
{
    private static readonly Regex VersionRegex = new(@"ffmpeg\s+version\s+([^\s,]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IProcessRunner _processRunner;

    public FFmpegAudioConverter(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DefaultProcessRunner();
    }

    public async Task<FFmpegStatus> GetStatusAsync(string? ffmpegPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return FFmpegStatus.NotConfigured;
        }

        string fullPath = Path.GetFullPath(ffmpegPath.Trim());
        if (!File.Exists(fullPath))
        {
            return FFmpegStatus.NotFound(fullPath);
        }

        try
        {
            ProcessResult result = await _processRunner.RunAsync(fullPath, ["-version"], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                return new FFmpegStatus(false, null, $"FFmpeg exited with code {result.ExitCode}: {error.Trim()}");
            }

            string output = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
            Match match = VersionRegex.Match(output);
            string version = match.Success ? match.Groups[1].Value : "Unknown version";
            return FFmpegStatus.Available(version);
        }
        catch (Exception ex)
        {
            return new FFmpegStatus(false, null, $"Could not run FFmpeg: {ex.Message}");
        }
    }

    public async Task<AudioConversionResult> ConvertToConsoleWavAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        BgmAudioChannels channels = BgmAudioChannels.Stereo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string ffmpegFullPath = Path.GetFullPath(ffmpegPath.Trim());
        if (!File.Exists(ffmpegFullPath))
        {
            return AudioConversionResult.Failed(outputPath, $"FFmpeg executable not found at '{ffmpegFullPath}'.");
        }

        string inputFullPath = Path.GetFullPath(inputPath.Trim());
        if (!File.Exists(inputFullPath))
        {
            return AudioConversionResult.Failed(outputPath, $"Input audio file not found at '{inputFullPath}'.");
        }

        string outputFullPath = Path.GetFullPath(outputPath.Trim());
        string? outputDir = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string tempOutputPath = Path.Combine(Path.GetTempPath(), $"mts_bgm_{Guid.NewGuid():N}.wav");

        try
        {
            string channelCount = channels == BgmAudioChannels.Mono ? "1" : "2";
            var arguments = new List<string>
            {
                "-y",
                "-i",
                inputFullPath,
                "-vn",
                "-c:a",
                "pcm_s16le",
                "-ar",
                "44100",
                "-ac",
                channelCount,
                tempOutputPath,
            };

            ProcessResult result = await _processRunner.RunAsync(ffmpegFullPath, arguments, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                return AudioConversionResult.Failed(outputFullPath, $"FFmpeg conversion failed (exit code {result.ExitCode}): {error.Trim()}");
            }

            if (!File.Exists(tempOutputPath))
            {
                return AudioConversionResult.Failed(outputFullPath, "FFmpeg completed without creating the output WAV file.");
            }

            WavAudioInfo audioInfo = WavAudioReader.Read(tempOutputPath);
            if (!audioInfo.IsValidWav || !audioInfo.IsConsoleStandard)
            {
                return AudioConversionResult.Failed(outputFullPath, $"Converted file is not valid console audio: {audioInfo.FormatDescription}");
            }

            int expectedChannels = channels == BgmAudioChannels.Mono ? 1 : 2;
            if (audioInfo.Channels != expectedChannels)
            {
                return AudioConversionResult.Failed(outputFullPath, $"Converted file channels mismatch: expected {expectedChannels}, got {audioInfo.Channels}.");
            }

            // Move safely to final output path
            File.Move(tempOutputPath, outputFullPath, overwrite: true);
            return AudioConversionResult.Succeeded(outputFullPath, audioInfo);
        }
        catch (Exception ex)
        {
            return AudioConversionResult.Failed(outputFullPath, $"Error during audio conversion: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempOutputPath))
            {
                try
                {
                    File.Delete(tempOutputPath);
                }
                catch
                {
                    // Ignore temp cleanup failures
                }
            }
        }
    }
}
