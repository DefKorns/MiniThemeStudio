using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Infrastructure.Audio;

namespace MiniThemeStudio.Infrastructure.Tests.Audio;

public sealed class BgmAudioValidatorTests
{
    [Fact]
    public void ValidatesConsoleStandardStereoWav()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var filePath = Path.Combine(tempDir, "bgm_home.wav");
            File.WriteAllBytes(filePath, WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 44100 * 3));

            var result = BgmAudioValidator.Validate(filePath, BgmAudioChannels.Stereo);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.NotNull(result.AudioInfo);
            Assert.Equal(2, result.AudioInfo.Channels);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ValidatesConsoleStandardMonoWav()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var filePath = Path.Combine(tempDir, "bgm_home.wav");
            File.WriteAllBytes(filePath, WavAudioReader.CreateMinimalPcm16Wav(44100, 1, 44100 * 3));

            var result = BgmAudioValidator.Validate(filePath, BgmAudioChannels.Mono);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.NotNull(result.AudioInfo);
            Assert.Equal(1, result.AudioInfo.Channels);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FlagsChannelLayoutMismatchAsWarningWhenPreferredGiven()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var filePath = Path.Combine(tempDir, "bgm_home.wav");
            // File is stereo, but user prefers mono
            File.WriteAllBytes(filePath, WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 44100));

            var result = BgmAudioValidator.Validate(filePath, BgmAudioChannels.Mono);

            Assert.True(result.IsValid);
            Assert.NotEmpty(result.Warnings);
            Assert.Contains(result.Warnings, w => w.Contains("preferred export channel layout is mono", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RejectsNonStandardSampleRate()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var filePath = Path.Combine(tempDir, "bgm_home.wav");
            File.WriteAllBytes(filePath, WavAudioReader.CreateMinimalPcm16Wav(22050, 2, 22050));

            var result = BgmAudioValidator.Validate(filePath);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("22050 Hz", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RecognizesSupportedNonWavFileForConversion()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var filePath = Path.Combine(tempDir, "bgm_home.mp3");
            File.WriteAllText(filePath, "dummy mp3 data");

            var result = BgmAudioValidator.Validate(filePath);

            Assert.False(result.IsValid);
            Assert.Empty(result.Errors); // Should be warning indicating conversion required
            Assert.Contains(result.Warnings, w => w.Contains("must be converted", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
