using System;
using System.IO;
using System.Text;
using MiniThemeStudio.Core.Fonts;

namespace MiniThemeStudio.Infrastructure.Fonts;

public static class MiniFontWriter
{
    private static readonly byte[] BmfMagic = [0x42, 0x4D, 0x46, 0x03]; // BMF\x03

    public static void Write(MiniFont font, string filePath)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(filePath);
        Write(font, stream);
    }

    public static byte[] ToBytes(MiniFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        using var stream = new MemoryStream();
        Write(font, stream);
        return stream.ToArray();
    }

    public static void Write(MiniFont font, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(stream);

        var bmfBytes = SerializeBmf(font);
        var textureBytes = font.TexturePngBytes ?? [];

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // 9-byte container header
        writer.Write(font.FormatType != 0 ? font.FormatType : (byte)1);
        writer.Write((uint)bmfBytes.Length);
        writer.Write((uint)textureBytes.Length);

        // Payloads
        writer.Write(bmfBytes);
        writer.Write(textureBytes);
    }

    private static byte[] SerializeBmf(MiniFont font)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        // BMF Header
        writer.Write(BmfMagic);

        // Block 1: Info
        WriteInfoBlock(writer, font.Info);

        // Block 2: Common
        WriteCommonBlock(writer, font.Common);

        // Block 3: Pages
        WritePagesBlock(writer, font.PageNames);

        // Block 4: Chars
        WriteCharsBlock(writer, font.Glyphs);

        // Block 5: Kerning (optional)
        if (font.KerningPairs is { Count: > 0 })
        {
            WriteKerningBlock(writer, font.KerningPairs);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static void WriteInfoBlock(BinaryWriter writer, FontInfo info)
    {
        var nameBytes = Encoding.UTF8.GetBytes(info.FontName ?? string.Empty);
        var blockSize = (uint)(14 + nameBytes.Length + 1);

        writer.Write((byte)1); // Block ID 1
        writer.Write(blockSize);

        writer.Write(info.FontSize);

        byte bitField = 0;
        if (info.Bold)
        {
            bitField |= 1 << 4;
        }
        if (info.Italic)
        {
            bitField |= 1 << 5;
        }
        if (info.Unicode)
        {
            bitField |= 1 << 6;
        }
        if (info.Smooth)
        {
            bitField |= 1 << 7;
        }
        writer.Write(bitField);

        writer.Write(info.Charset);
        writer.Write(info.StretchH);
        writer.Write(info.Aa);
        writer.Write(info.PaddingUp);
        writer.Write(info.PaddingRight);
        writer.Write(info.PaddingDown);
        writer.Write(info.PaddingLeft);
        writer.Write(info.SpacingHoriz);
        writer.Write(info.SpacingVert);
        writer.Write(info.Outline);

        writer.Write(nameBytes);
        writer.Write((byte)0); // Null terminator
    }

    private static void WriteCommonBlock(BinaryWriter writer, FontCommon common)
    {
        const uint blockSize = 15;
        writer.Write((byte)2); // Block ID 2
        writer.Write(blockSize);

        writer.Write((ushort)common.LineHeight);
        writer.Write((ushort)common.Base);
        writer.Write((ushort)common.ScaleW);
        writer.Write((ushort)common.ScaleH);
        writer.Write((ushort)Math.Max(1, common.Pages));

        byte bitField = 0;
        if (common.Packed)
        {
            bitField |= 1 << 7;
        }
        writer.Write(bitField);

        writer.Write(common.AlphaChannel);
        writer.Write(common.RedChannel);
        writer.Write(common.GreenChannel);
        writer.Write(common.BlueChannel);
    }

    private static void WritePagesBlock(BinaryWriter writer, System.Collections.Generic.IReadOnlyList<string>? pageNames)
    {
        using var ms = new MemoryStream();
        if (pageNames is { Count: > 0 })
        {
            foreach (var name in pageNames)
            {
                var bytes = Encoding.UTF8.GetBytes(name);
                ms.Write(bytes);
                ms.WriteByte(0);
            }
        }
        else
        {
            // At least one page name (null terminated)
            ms.WriteByte(0);
        }

        var pagesBytes = ms.ToArray();
        writer.Write((byte)3); // Block ID 3
        writer.Write((uint)pagesBytes.Length);
        writer.Write(pagesBytes);
    }

    private static void WriteCharsBlock(BinaryWriter writer, System.Collections.Generic.IReadOnlyList<FontGlyph>? glyphs)
    {
        var list = glyphs ?? [];
        var blockSize = (uint)(list.Count * 20);

        writer.Write((byte)4); // Block ID 4
        writer.Write(blockSize);

        foreach (var g in list)
        {
            writer.Write(g.Id);
            writer.Write((ushort)g.X);
            writer.Write((ushort)g.Y);
            writer.Write((ushort)g.Width);
            writer.Write((ushort)g.Height);
            writer.Write((short)g.XOffset);
            writer.Write((short)g.YOffset);
            writer.Write((short)g.XAdvance);
            writer.Write((byte)g.Page);
            writer.Write((byte)g.Channel);
        }
    }

    private static void WriteKerningBlock(BinaryWriter writer, System.Collections.Generic.IReadOnlyList<FontKerningPair> kerningPairs)
    {
        var blockSize = (uint)(kerningPairs.Count * 10);
        writer.Write((byte)5); // Block ID 5
        writer.Write(blockSize);

        foreach (var k in kerningPairs)
        {
            writer.Write(k.First);
            writer.Write(k.Second);
            writer.Write((short)k.Amount);
        }
    }
}
