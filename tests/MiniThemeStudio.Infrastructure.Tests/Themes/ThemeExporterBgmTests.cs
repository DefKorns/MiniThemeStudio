using System.Formats.Tar;
using System.IO.Compression;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Audio;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeExporterBgmTests
{
    private sealed class MockAudioConverter : IFFmpegAudioConverter
    {
        public int ConversionCallCount { get; private set; }

        public Task<FFmpegStatus> GetStatusAsync(string? ffmpegPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FFmpegStatus.Available("6.1"));
        }

        public Task<AudioConversionResult> ConvertToConsoleWavAsync(
            string ffmpegPath,
            string inputPath,
            string outputPath,
            BgmAudioChannels channels = BgmAudioChannels.Stereo,
            CancellationToken cancellationToken = default)
        {
            ConversionCallCount++;
            short expectedChannels = channels == BgmAudioChannels.Mono ? (short)1 : (short)2;
            File.WriteAllBytes(outputPath, WavAudioReader.CreateMinimalPcm16Wav(44100, expectedChannels, 44100));
            WavAudioInfo info = WavAudioReader.Read(outputPath);
            return Task.FromResult(AudioConversionResult.Succeeded(outputPath, info));
        }
    }

    [Fact]
    public void DisabledBgmConversionPreservesFilesUnchangedWithoutCallingConverter()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string themeDir = Path.Combine(tempDir, "theme");
            string outDir = Path.Combine(tempDir, "out");
            string soundsDir = Path.Combine(themeDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);

            byte[] rawBytes = "original-audio-bytes"u8.ToArray();
            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), rawBytes);

            var mockConverter = new MockAudioConverter();
            var exporter = new ThemeExporter(mockConverter);

            var options = new ThemeExportOptions(ConvertBgmAudio: false);
            ThemePackageResult result = exporter.ExportPackage(themeDir, outDir, "snes", "mytheme", null, null, options);

            Assert.Equal(0, mockConverter.ConversionCallCount);
            Assert.True(File.Exists(result.PackagePath));

            // Verify tar.gz contains the original bytes
            using FileStream fileStream = File.OpenRead(result.PackagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new TarReader(gzipStream);

            TarEntry? entry;
            bool found = false;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (entry.Name.EndsWith("bgm_home.wav", StringComparison.Ordinal))
                {
                    found = true;
                    using var ms = new MemoryStream();
                    entry.DataStream!.CopyTo(ms);
                    Assert.Equal(rawBytes, ms.ToArray());
                }
            }

            Assert.True(found);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EnabledBgmConversionConvertsAndOmitsNonWavSourceFromTar()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string themeDir = Path.Combine(tempDir, "theme");
            string outDir = Path.Combine(tempDir, "out");
            string soundsDir = Path.Combine(themeDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);

            // Input is an MP3 file
            File.WriteAllText(Path.Combine(soundsDir, "bgm_home.mp3"), "mp3 data");

            string fakeFFmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(fakeFFmpeg, "stub");

            var mockConverter = new MockAudioConverter();
            var exporter = new ThemeExporter(mockConverter);

            var options = new ThemeExportOptions(
                ConvertBgmAudio: true,
                FFmpegPath: fakeFFmpeg,
                BgmChannels: BgmAudioChannels.Mono);

            ThemePackageResult result = exporter.ExportPackage(themeDir, outDir, "snes", "mytheme", null, null, options);

            Assert.Equal(1, mockConverter.ConversionCallCount);
            Assert.True(File.Exists(Path.Combine(soundsDir, "bgm_home.wav")));

            // Inspect the archive: it MUST contain bgm_home.wav and MUST NOT contain bgm_home.mp3
            using FileStream fileStream = File.OpenRead(result.PackagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new TarReader(gzipStream);

            TarEntry? entry;
            bool hasWav = false;
            bool hasMp3 = false;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (entry.Name.EndsWith("bgm_home.wav", StringComparison.Ordinal))
                {
                    hasWav = true;
                }
                if (entry.Name.EndsWith("bgm_home.mp3", StringComparison.Ordinal))
                {
                    hasMp3 = true;
                }
            }

            Assert.True(hasWav, "Export package must contain converted bgm_home.wav");
            Assert.False(hasMp3, "Export package must NOT contain non-wav bgm_home.mp3");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DisabledIncludeBgmOmitsBgmTracksAndDoesNotCallConverter()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string themeDir = Path.Combine(tempDir, "theme");
            string outDir = Path.Combine(tempDir, "out");
            string soundsDir = Path.Combine(themeDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(soundsDir);

            File.WriteAllBytes(Path.Combine(soundsDir, "bgm_home.wav"), WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 44100));

            string fakeFFmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(fakeFFmpeg, "stub");

            var mockConverter = new MockAudioConverter();
            var exporter = new ThemeExporter(mockConverter);

            var options = new ThemeExportOptions(
                ConvertBgmAudio: true,
                FFmpegPath: fakeFFmpeg,
                BgmChannels: BgmAudioChannels.Mono,
                IncludeBgm: false);

            ThemePackageResult result = exporter.ExportPackage(themeDir, outDir, "snes", "mytheme", null, null, options);

            Assert.Equal(0, mockConverter.ConversionCallCount);

            using FileStream fileStream = File.OpenRead(result.PackagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new TarReader(gzipStream);

            TarEntry? entry;
            bool foundBgm = false;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (entry.Name.Contains("bgm_home", StringComparison.OrdinalIgnoreCase))
                {
                    foundBgm = true;
                }
            }

            Assert.False(foundBgm, "Package must not include any BGM tracks when IncludeBgm is false.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeltaThemeWithUntouchedBaseBgmDoesNotConvertOrExportBaseBgm()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string themeDir = Path.Combine(tempDir, "theme");
            string baseDir = Path.Combine(tempDir, "base");
            string outDir = Path.Combine(tempDir, "out");

            string themeSoundsDir = Path.Combine(themeDir, "resources", "sounds", "hvc");
            string baseSoundsDir = Path.Combine(baseDir, "resources", "sounds", "hvc");
            Directory.CreateDirectory(themeSoundsDir);
            Directory.CreateDirectory(baseSoundsDir);

            // Both have identical stereo WAV
            byte[] stockAudio = WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 44100);
            File.WriteAllBytes(Path.Combine(themeSoundsDir, "bgm_home.wav"), stockAudio);
            File.WriteAllBytes(Path.Combine(baseSoundsDir, "bgm_home.wav"), stockAudio);

            // Add an altered file so the theme is not empty
            File.WriteAllText(Path.Combine(themeDir, "custom.txt"), "hello");

            string fakeFFmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(fakeFFmpeg, "stub");

            var mockConverter = new MockAudioConverter();
            var exporter = new ThemeExporter(mockConverter);

            // ConvertBgmAudio is enabled with Mono, but bgm_home is untouched from base
            var options = new ThemeExportOptions(
                ConvertBgmAudio: true,
                FFmpegPath: fakeFFmpeg,
                BgmChannels: BgmAudioChannels.Mono,
                IncludeBgm: true);

            ThemePackageResult result = exporter.ExportPackage(themeDir, outDir, "snes", "mytheme", null, baseDir, options);

            // Converter must NOT be called for untouched base file
            Assert.Equal(0, mockConverter.ConversionCallCount);

            // Archive must NOT contain bgm_home.wav (it was skipped as unchanged from base)
            using FileStream fileStream = File.OpenRead(result.PackagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new TarReader(gzipStream);

            TarEntry? entry;
            bool foundBgm = false;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (entry.Name.Contains("bgm_home", StringComparison.OrdinalIgnoreCase))
                {
                    foundBgm = true;
                }
            }

            Assert.False(foundBgm, "Untouched base BGM must be omitted from delta package.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
