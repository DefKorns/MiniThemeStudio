using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Infrastructure.Audio;

namespace MiniThemeStudio.Infrastructure.Tests.Audio;

public sealed class WavAudioReaderTests
{
    [Fact]
    public void ReadsValidPcmStereoWav()
    {
        int sampleRate = 44100;
        short channels = (short)2;
        int sampleFrames = 44100 * 2; // 2 seconds
        byte[] bytes = WavAudioReader.CreateMinimalPcm16Wav(sampleRate, channels, sampleFrames);

        using var ms = new MemoryStream(bytes);
        WavAudioInfo info = WavAudioReader.Read(ms);

        Assert.True(info.IsValidWav);
        Assert.True(info.IsPcm);
        Assert.True(info.IsConsoleStandard);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(2.0, info.Duration.TotalSeconds, precision: 1);
        Assert.False(info.HasSmplChunk);
        Assert.Null(info.LoopStartSample);
        Assert.Null(info.LoopEndSample);
    }

    [Fact]
    public void ReadsValidPcmMonoWavWithSmplLoopPoints()
    {
        int sampleRate = 44100;
        short channels = (short)1;
        int sampleFrames = 44100 * 5; // 5 seconds
        byte[] bytes = WavAudioReader.CreateMinimalPcm16Wav(sampleRate, channels, sampleFrames, loopStart: 1000, loopEnd: 40000);

        using var ms = new MemoryStream(bytes);
        WavAudioInfo info = WavAudioReader.Read(ms);

        Assert.True(info.IsValidWav);
        Assert.True(info.IsPcm);
        Assert.True(info.IsConsoleStandard);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(5.0, info.Duration.TotalSeconds, precision: 1);
        Assert.True(info.HasSmplChunk);
        Assert.Equal(1000, info.LoopStartSample);
        Assert.Equal(40000, info.LoopEndSample);
    }

    [Fact]
    public void RejectsTruncatedWav()
    {
        byte[] bytes = new byte[20];
        using var ms = new MemoryStream(bytes);
        WavAudioInfo info = WavAudioReader.Read(ms);

        Assert.False(info.IsValidWav);
        Assert.Contains("too short", info.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNonRiffContainer()
    {
        byte[] bytes = new byte[64];
        using var ms = new MemoryStream(bytes);
        WavAudioInfo info = WavAudioReader.Read(ms);

        Assert.False(info.IsValidWav);
        Assert.Contains("Not a RIFF/WAVE container", info.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectsNonStandardSampleRateAndChannels()
    {
        // 48 kHz, 6 channels (5.1)
        byte[] bytes = WavAudioReader.CreateMinimalPcm16Wav(48000, 6, 48000);
        using var ms = new MemoryStream(bytes);
        WavAudioInfo info = WavAudioReader.Read(ms);

        Assert.True(info.IsValidWav);
        Assert.False(info.IsConsoleStandard);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(6, info.Channels);
    }
}
