using System.Buffers.Binary;
using System.Text;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ConsolePngTests
{
    private static readonly string[] ColourChunks = ["gAMA", "iCCP", "sRGB", "cHRM"];

    [Fact]
    public void EncodeWritesEightBitRgbaWithoutInterlace()
    {
        using var source = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Gray8, SKAlphaType.Opaque));
        source.Erase(new SKColor(90, 90, 90));

        byte[] png = Encoded(source);

        (int bitDepth, int colourType, int interlace) = Header(png);
        Assert.Equal(8, bitDepth);
        Assert.Equal(6, colourType);
        Assert.Equal(0, interlace);
    }

    [Fact]
    public void EncodeDropsTheColourProfileAndGammaChunksOfTheSource()
    {
        var profile = SKColorSpace.CreateRgb(new SKColorSpaceTransferFn { A = 1, G = 1.8f }, SKColorSpaceXyz.Srgb);
        using var source = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul, profile));
        source.Erase(new SKColor(200, 100, 50, 255));
        using SKData tagged = SKImage.FromBitmap(source).Encode(SKEncodedImageFormat.Png, 100);
        Assert.Contains(Chunks(tagged.ToArray()), name => ColourChunks.Contains(name)); // the source carries one

        using SKBitmap decoded = SKBitmap.Decode(tagged);
        byte[] png = Encoded(decoded);

        Assert.DoesNotContain(Chunks(png), name => ColourChunks.Contains(name));
    }

    [Fact]
    public void EncodeKeepsThePixelsAndTheirAlpha()
    {
        using var source = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        source.SetPixel(0, 0, new SKColor(10, 20, 30, 255));
        source.SetPixel(1, 0, new SKColor(40, 50, 60, 128));

        using SKBitmap decoded = SKBitmap.Decode(Encoded(source));

        Assert.Equal(new SKColor(10, 20, 30, 255), decoded.GetPixel(0, 0));
        Assert.Equal(new SKColor(40, 50, 60, 128), decoded.GetPixel(1, 0));
    }

    [Fact]
    public void SaveWritesTheFileAndLeavesNoTemporaryOne()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = Path.Combine(root, "new", "texture.png");
            using var source = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            source.Erase(SKColors.Red);

            ConsolePng.Save(source, path);

            Assert.Equal(new SKColor(255, 0, 0, 255), SKBitmap.Decode(path).GetPixel(0, 0));
            Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static byte[] Encoded(SKBitmap bitmap)
    {
        using SKData data = ConsolePng.Encode(bitmap);
        return data.ToArray();
    }

    private static (int BitDepth, int ColourType, int Interlace) Header(byte[] png) => (png[24], png[25], png[28]);

    private static List<string> Chunks(byte[] png)
    {
        var names = new List<string>();
        int offset = 8; // after the signature
        while (offset + 8 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            names.Add(Encoding.ASCII.GetString(png, offset + 4, 4));
            offset += 12 + length;
        }

        return names;
    }
}
