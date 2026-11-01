using MiniThemeStudio.Core.Audio;

namespace MiniThemeStudio.Infrastructure.Audio;

public sealed record BgmValidationResult(
    bool IsValid,
    WavAudioInfo? AudioInfo,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public static class BgmAudioValidator
{
    private static readonly HashSet<string> SupportedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff"
    };

    public static bool IsSupportedAudioFile(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && SupportedInputExtensions.Contains(ext);
    }

    public static BgmValidationResult Validate(string audioFilePath, BgmAudioChannels? preferredChannels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (!File.Exists(audioFilePath))
        {
            errors.Add($"Audio file not found: '{audioFilePath}'");
            return new BgmValidationResult(false, null, errors, warnings);
        }

        string fileName = Path.GetFileName(audioFilePath);
        string ext = Path.GetExtension(audioFilePath);

        if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            if (SupportedInputExtensions.Contains(ext))
            {
                warnings.Add($"'{fileName}' is a {ext.ToUpperInvariant()} audio file. It must be converted to PCM 16-bit 44.1 kHz WAV before console playback.");
                return new BgmValidationResult(false, null, errors, warnings);
            }

            errors.Add($"'{fileName}' has an unsupported audio format ({ext}).");
            return new BgmValidationResult(false, null, errors, warnings);
        }

        WavAudioInfo info = WavAudioReader.Read(audioFilePath);
        if (!info.IsValidWav)
        {
            errors.Add($"'{fileName}' is corrupt or not a valid WAV file: {info.ErrorMessage}");
            return new BgmValidationResult(false, info, errors, warnings);
        }

        if (!info.IsPcm)
        {
            errors.Add($"'{fileName}' is not PCM format (format code {info.AudioFormat}). Console requires uncompressed PCM (s16le).");
        }

        if (info.SampleRate != 44100 && info.SampleRate != 32000 && info.SampleRate != 48000)
        {
            errors.Add($"'{fileName}' has sample rate {info.SampleRate} Hz. Console requires 44.1 kHz or 32 kHz PCM audio.");
        }

        if (info.BitsPerSample != 16)
        {
            errors.Add($"'{fileName}' bit depth is {info.BitsPerSample}-bit. Console requires 16-bit PCM.");
        }

        if (info.Channels != 1 && info.Channels != 2)
        {
            errors.Add($"'{fileName}' has {info.Channels} audio channels. Console supports mono (1 ch) or stereo (2 ch).");
        }
        else if (preferredChannels.HasValue)
        {
            int expected = preferredChannels.Value == BgmAudioChannels.Mono ? 1 : 2;
            if (info.Channels != expected)
            {
                warnings.Add($"'{fileName}' is {(info.Channels == 1 ? "mono" : "stereo")}, but preferred export channel layout is {(expected == 1 ? "mono" : "stereo")}.");
            }
        }

        if (info.Duration > TimeSpan.FromMinutes(10))
        {
            warnings.Add($"'{fileName}' duration ({info.Duration.TotalMinutes:F1} min) is unusually long for menu BGM and may consume excessive console RAM.");
        }

        if (info.DataSizeBytes > 25 * 1024 * 1024)
        {
            warnings.Add($"'{fileName}' size ({info.DataSizeBytes / 1024.0 / 1024.0:F1} MB) is large and may risk memory limits on the console.");
        }

        bool isValid = errors.Count == 0;
        return new BgmValidationResult(isValid, info, errors, warnings);
    }

    public static bool IsSilentPcm(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 44)
            {
                return false;
            }

            byte[] riff = reader.ReadBytes(4);
            if (!riff.SequenceEqual("RIFF"u8))
            {
                return false;
            }

            _ = reader.ReadInt32();
            byte[] wave = reader.ReadBytes(4);
            if (!wave.SequenceEqual("WAVE"u8))
            {
                return false;
            }

            while (stream.Position + 8 <= stream.Length)
            {
                byte[] chunkId = reader.ReadBytes(4);
                int chunkSize = reader.ReadInt32();
                if (chunkId.SequenceEqual("data"u8))
                {
                    if (chunkSize <= 0)
                    {
                        return false;
                    }

                    int bytesToCheck = Math.Min(chunkSize, 65536);
                    byte[] buffer = reader.ReadBytes(bytesToCheck);
                    foreach (byte b in buffer)
                    {
                        if (b != 0)
                        {
                            return false;
                        }
                    }

                    return buffer.Length > 0;
                }

                if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                {
                    break;
                }

                stream.Seek(chunkSize, SeekOrigin.Current);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
