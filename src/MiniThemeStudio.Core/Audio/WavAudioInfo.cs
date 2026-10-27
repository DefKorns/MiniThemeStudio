namespace MiniThemeStudio.Core.Audio;

public sealed record WavAudioInfo(
    bool IsValidWav,
    short AudioFormat,
    short Channels,
    int SampleRate,
    short BitsPerSample,
    long DataSizeBytes,
    TimeSpan Duration,
    bool HasSmplChunk = false,
    int? LoopStartSample = null,
    int? LoopEndSample = null,
    string? ErrorMessage = null)
{
    public static WavAudioInfo Invalid(string errorMessage) =>
        new(
            IsValidWav: false,
            AudioFormat: 0,
            Channels: 0,
            SampleRate: 0,
            BitsPerSample: 0,
            DataSizeBytes: 0,
            Duration: TimeSpan.Zero,
            ErrorMessage: errorMessage);

    public bool IsPcm => AudioFormat == 1;

    public bool IsConsoleStandard =>
        IsValidWav &&
        IsPcm &&
        SampleRate == 44100 &&
        BitsPerSample == 16 &&
        (Channels == 1 || Channels == 2);

    public string FormatDescription =>
        !IsValidWav
            ? $"Invalid WAV: {ErrorMessage}"
            : $"{(IsPcm ? "PCM" : $"Format({AudioFormat})")}, {SampleRate} Hz, {BitsPerSample}-bit, {(Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels} ch")}" +
              (Duration > TimeSpan.Zero ? $" ({Duration.TotalSeconds:F1}s, {DataSizeBytes / 1024.0 / 1024.0:F2} MB)" : "");
}
