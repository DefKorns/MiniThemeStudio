using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Audio;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeValidatorBgmTests
{
    private static void SetupBasicThemeStructure(string themeDir)
    {
        string spritesDir = Path.Combine(themeDir, "resources", "sprites");
        Directory.CreateDirectory(spritesDir);

        using var bitmap = new SKBitmap(100, 100);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(Path.Combine(spritesDir, "packed.png"));
        data.SaveTo(stream);

        File.WriteAllText(
            Path.Combine(spritesDir, "packed.json"),
            """
            {
              "texture": { "id": "test", "width": 100, "height": 100 },
              "sprites": {
                "icon": { "textureFrame": [0, 0, 50, 50], "pivot": [0.5, 0.5] }
              }
            }
            """);
    }

    [Fact]
    public void ValidWavBgmPassesValidation()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            SetupBasicThemeStructure(tempDir);
            string soundsDir = Path.Combine(tempDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);
            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 44100));

            var validator = new ThemeValidator();
            ThemeValidationResult result = validator.Validate(tempDir);

            Assert.True(result.IsValid, string.Join("\n", result.Issues.Where(i => i.IsError).Select(i => i.Message)));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void NonConformingBgmFailsValidationWhenConversionDisabled()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            SetupBasicThemeStructure(tempDir);
            string soundsDir = Path.Combine(tempDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);
            // 22050 Hz is not a valid console rate
            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), WavAudioReader.CreateMinimalPcm16Wav(22050, 2, 22050));

            var validator = new ThemeValidator();
            ThemeValidationResult result = validator.Validate(tempDir, options: new ThemeValidationOptions(ConvertBgmAudio: false));

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.IsError && i.Message.Contains("22050 Hz"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void NonConformingBgmPassesAsNoticeWhenConversionEnabledWithFFmpeg()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            SetupBasicThemeStructure(tempDir);
            string soundsDir = Path.Combine(tempDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);
            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), WavAudioReader.CreateMinimalPcm16Wav(22050, 2, 22050));

            string fakeFFmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(fakeFFmpeg, "stub");

            var validator = new ThemeValidator();
            var options = new ThemeValidationOptions(
                ConvertBgmAudio: true,
                FFmpegPath: fakeFFmpeg,
                BgmChannels: BgmAudioChannels.Stereo);
            ThemeValidationResult result = validator.Validate(tempDir, options: options);

            Assert.True(result.IsValid, string.Join("\n", result.Issues.Where(i => i.IsError).Select(i => i.Message)));
            Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("will be converted", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FailsWhenConversionEnabledButFFmpegNotConfigured()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            SetupBasicThemeStructure(tempDir);
            string soundsDir = Path.Combine(tempDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);
            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), WavAudioReader.CreateMinimalPcm16Wav(22050, 2, 22050));

            var validator = new ThemeValidator();
            var options = new ThemeValidationOptions(
                ConvertBgmAudio: true,
                FFmpegPath: null); // Not configured
            ThemeValidationResult result = validator.Validate(tempDir, options: options);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.IsError && i.Message.Contains("FFmpeg is not configured", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void NonConformingBgmPassesValidationWhenIncludeBgmIsFalse()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            SetupBasicThemeStructure(tempDir);
            string soundsDir = Path.Combine(tempDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);
            // Non-standard WAV rate
            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), WavAudioReader.CreateMinimalPcm16Wav(22050, 2, 22050));

            var validator = new ThemeValidator();
            var options = new ThemeValidationOptions(IncludeBgm: false);
            ThemeValidationResult result = validator.Validate(tempDir, options: options);

            Assert.True(result.IsValid);
            Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("excluded from export", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
