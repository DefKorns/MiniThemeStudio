using MiniThemeStudio.Core.Audio;

namespace MiniThemeStudio.Infrastructure.Audio;

public static class WavAudioReader
{
    private static readonly byte[] RiffHeader = "RIFF"u8.ToArray();
    private static readonly byte[] WaveHeader = "WAVE"u8.ToArray();
    private static readonly byte[] FmtChunk = "fmt "u8.ToArray();
    private static readonly byte[] DataChunk = "data"u8.ToArray();
    private static readonly byte[] SmplChunk = "smpl"u8.ToArray();

    public static WavAudioInfo Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return WavAudioInfo.Invalid($"File not found: '{filePath}'");
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return Read(stream);
        }
        catch (Exception ex)
        {
            return WavAudioInfo.Invalid($"Failed to read WAV file: {ex.Message}");
        }
    }

    public static WavAudioInfo Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.Length < 44)
        {
            return WavAudioInfo.Invalid("File is too short to be a valid WAV file.");
        }

        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        Span<byte> header = stackalloc byte[12];
        if (reader.Read(header) != 12)
        {
            return WavAudioInfo.Invalid("Unable to read RIFF header.");
        }

        if (!header[..4].SequenceEqual(RiffHeader) || !header[8..12].SequenceEqual(WaveHeader))
        {
            return WavAudioInfo.Invalid("Not a RIFF/WAVE container.");
        }

        short audioFormat = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        long dataSizeBytes = 0;
        bool hasFmt = false;
        bool hasData = false;
        bool hasSmpl = false;
        int? loopStart = null;
        int? loopEnd = null;

        while (stream.Position < stream.Length - 8)
        {
            var chunkId = reader.ReadBytes(4);
            if (chunkId.Length < 4)
            {
                break;
            }

            var chunkSize = reader.ReadUInt32();
            var nextChunkPosition = stream.Position + chunkSize;

            if (chunkId.SequenceEqual(FmtChunk))
            {
                if (chunkSize < 16)
                {
                    return WavAudioInfo.Invalid("fmt chunk is invalid or too small.");
                }

                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32(); // byteRate
                _ = reader.ReadInt16(); // blockAlign
                bitsPerSample = reader.ReadInt16();
                hasFmt = true;
            }
            else if (chunkId.SequenceEqual(DataChunk))
            {
                dataSizeBytes = chunkSize;
                hasData = true;
            }
            else if (chunkId.SequenceEqual(SmplChunk) && chunkSize >= 36)
            {
                hasSmpl = true;
                _ = reader.ReadBytes(28); // manufacturer, product, samplePeriod, unityNote, pitchFraction, smpteFmt, smpteOffset
                var numLoops = reader.ReadInt32();
                _ = reader.ReadInt32(); // samplerData

                if (numLoops > 0 && chunkSize >= 36 + 24)
                {
                    _ = reader.ReadInt32(); // cuePointId
                    _ = reader.ReadInt32(); // type
                    loopStart = reader.ReadInt32();
                    loopEnd = reader.ReadInt32();
                }
            }

            if (nextChunkPosition > stream.Length)
            {
                break;
            }

            stream.Seek(nextChunkPosition, SeekOrigin.Begin);
        }

        if (!hasFmt)
        {
            return WavAudioInfo.Invalid("Missing 'fmt ' chunk in WAV file.");
        }

        if (!hasData)
        {
            return WavAudioInfo.Invalid("Missing 'data' chunk in WAV file.");
        }

        var bytesPerSample = bitsPerSample / 8;
        var bytesPerFrame = channels * bytesPerSample;
        var duration = bytesPerFrame > 0 && sampleRate > 0
            ? TimeSpan.FromSeconds((double)dataSizeBytes / (sampleRate * bytesPerFrame))
            : TimeSpan.Zero;

        return new WavAudioInfo(
            IsValidWav: true,
            AudioFormat: audioFormat,
            Channels: channels,
            SampleRate: sampleRate,
            BitsPerSample: bitsPerSample,
            DataSizeBytes: dataSizeBytes,
            Duration: duration,
            HasSmplChunk: hasSmpl,
            LoopStartSample: loopStart,
            LoopEndSample: loopEnd);
    }

    public static byte[] CreateMinimalPcm16Wav(
        int sampleRate,
        short channels,
        int sampleFrames,
        int? loopStart = null,
        int? loopEnd = null)
    {
        var bytesPerSample = (short)2;
        var blockAlign = (short)(channels * bytesPerSample);
        var byteRate = sampleRate * blockAlign;
        var dataSize = sampleFrames * blockAlign;

        var hasSmpl = loopStart.HasValue && loopEnd.HasValue;
        var smplSize = hasSmpl ? 60 : 0;
        var totalRiffSize = 36 + dataSize + (hasSmpl ? (8 + smplSize) : 0);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.ASCII);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write((uint)totalRiffSize);
        writer.Write("WAVE"u8);

        // fmt chunk
        writer.Write("fmt "u8);
        writer.Write((uint)16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)16); // 16 bits

        // data chunk
        writer.Write("data"u8);
        writer.Write((uint)dataSize);
        writer.Write(new byte[dataSize]);

        // optional smpl chunk
        if (hasSmpl)
        {
            writer.Write("smpl"u8);
            writer.Write((uint)smplSize);
            writer.Write((uint)0); // manufacturer
            writer.Write((uint)0); // product
            writer.Write((uint)(1_000_000_000 / sampleRate)); // sample period in ns
            writer.Write((uint)60); // MIDI unity note
            writer.Write((uint)0); // pitch fraction
            writer.Write((uint)0); // SMPTE format
            writer.Write((uint)0); // SMPTE offset
            writer.Write((uint)1); // num loops
            writer.Write((uint)0); // sampler data size
            // Loop 0
            writer.Write((uint)0); // cue point id
            writer.Write((uint)0); // loop type: 0 = forward
            writer.Write((uint)loopStart!.Value);
            writer.Write((uint)loopEnd!.Value);
            writer.Write((uint)0); // fraction
            writer.Write((uint)0); // play count: 0 = infinite
        }

        return ms.ToArray();
    }
}
