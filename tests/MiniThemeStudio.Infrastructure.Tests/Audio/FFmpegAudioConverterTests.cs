using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Infrastructure.Audio;

namespace MiniThemeStudio.Infrastructure.Tests.Audio;

public sealed class FFmpegAudioConverterTests
{
    private sealed class MockProcessRunner : IProcessRunner
    {
        public Func<string, IReadOnlyList<string>, ProcessResult>? Handler { get; set; }
        public List<(string Executable, IReadOnlyList<string> Arguments)> Executions { get; } = [];

        public Task<ProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Executions.Add((executablePath, arguments));
            var result = Handler != null
                ? Handler(executablePath, arguments)
                : new ProcessResult(0, "ffmpeg version 6.1.1-full_build Copyright", "");
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task GetStatusReturnsNotConfiguredWhenEmpty()
    {
        var converter = new FFmpegAudioConverter(new MockProcessRunner());
        var status = await converter.GetStatusAsync("");

        Assert.False(status.IsAvailable);
        Assert.Contains("not configured", status.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusReturnsNotFoundWhenFileDoesNotExist()
    {
        var converter = new FFmpegAudioConverter(new MockProcessRunner());
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_ffmpeg_{Guid.NewGuid():N}.exe");

        var status = await converter.GetStatusAsync(nonExistentPath);

        Assert.False(status.IsAvailable);
        Assert.Contains("not found", status.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusParsesVersionOutputSuccessfully()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var mock = new MockProcessRunner
            {
                Handler = (_, _) => new ProcessResult(0, "ffmpeg version 7.0.2-static https://johnvansickle.com/ffmpeg/", "")
            };

            var converter = new FFmpegAudioConverter(mock);
            var status = await converter.GetStatusAsync(tempFile);

            Assert.True(status.IsAvailable);
            Assert.Equal("7.0.2-static", status.Version);
            Assert.Single(mock.Executions);
            Assert.Equal("-version", mock.Executions[0].Arguments[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConvertsToMonoWavWithExpectedArgumentsAndPreservesSource()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ffmpegExe = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(ffmpegExe, "stub");

            var inputFile = Path.Combine(tempDir, "source_track.mp3");
            var originalContent = "source-mp3-bytes-that-must-be-preserved"u8.ToArray();
            File.WriteAllBytes(inputFile, originalContent);

            var outputFile = Path.Combine(tempDir, "bgm_home.wav");

            var mock = new MockProcessRunner
            {
                Handler = (_, args) =>
                {
                    // The last argument is the temporary output path
                    var tempOut = args[^1];
                    // Write valid 44.1 kHz 16-bit mono WAV to simulate successful FFmpeg output
                    File.WriteAllBytes(tempOut, WavAudioReader.CreateMinimalPcm16Wav(44100, 1, 44100));
                    return new ProcessResult(0, "", "");
                }
            };

            var converter = new FFmpegAudioConverter(mock);
            var result = await converter.ConvertToConsoleWavAsync(ffmpegExe, inputFile, outputFile, BgmAudioChannels.Mono);

            Assert.True(result.Success);
            Assert.True(File.Exists(outputFile));
            // Verify source file was untouched and preserved
            Assert.Equal(originalContent, File.ReadAllBytes(inputFile));

            // Verify arguments passed to FFmpeg
            var executedArgs = mock.Executions[0].Arguments;
            Assert.Contains("-y", executedArgs);
            Assert.Contains("-i", executedArgs);
            Assert.Contains(inputFile, executedArgs);
            Assert.Contains("-vn", executedArgs);
            Assert.Contains("-c:a", executedArgs);
            Assert.Contains("pcm_s16le", executedArgs);
            Assert.Contains("-ar", executedArgs);
            Assert.Contains("44100", executedArgs);
            Assert.Contains("-ac", executedArgs);
            Assert.Contains("1", executedArgs); // Mono

            var outInfo = WavAudioReader.Read(outputFile);
            Assert.True(outInfo.IsConsoleStandard);
            Assert.Equal(1, outInfo.Channels);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConvertsToStereoWavWithExpectedArguments()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ffmpegExe = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(ffmpegExe, "stub");

            var inputFile = Path.Combine(tempDir, "source_track.ogg");
            File.WriteAllText(inputFile, "ogg data");

            var outputFile = Path.Combine(tempDir, "bgm_boot.wav");

            var mock = new MockProcessRunner
            {
                Handler = (_, args) =>
                {
                    var tempOut = args[^1];
                    File.WriteAllBytes(tempOut, WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 44100));
                    return new ProcessResult(0, "", "");
                }
            };

            var converter = new FFmpegAudioConverter(mock);
            var result = await converter.ConvertToConsoleWavAsync(ffmpegExe, inputFile, outputFile, BgmAudioChannels.Stereo);

            Assert.True(result.Success);
            Assert.True(File.Exists(outputFile));

            var executedArgs = mock.Executions[0].Arguments;
            Assert.Contains("-ac", executedArgs);
            Assert.Contains("2", executedArgs); // Stereo

            var outInfo = WavAudioReader.Read(outputFile);
            Assert.True(outInfo.IsConsoleStandard);
            Assert.Equal(2, outInfo.Channels);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task HandlesFFmpegFailureGracefully()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ffmpegExe = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(ffmpegExe, "stub");

            var inputFile = Path.Combine(tempDir, "corrupted.mp3");
            File.WriteAllText(inputFile, "bad data");

            var outputFile = Path.Combine(tempDir, "bgm_home.wav");

            var mock = new MockProcessRunner
            {
                Handler = (_, _) => new ProcessResult(1, "", "Invalid data found when processing input")
            };

            var converter = new FFmpegAudioConverter(mock);
            var result = await converter.ConvertToConsoleWavAsync(ffmpegExe, inputFile, outputFile, BgmAudioChannels.Stereo);

            Assert.False(result.Success);
            Assert.Contains("Invalid data found", result.ErrorMessage);
            Assert.False(File.Exists(outputFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
