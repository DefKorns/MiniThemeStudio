using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MiniThemeStudio.Core.Fonts;

namespace MiniThemeStudio.Infrastructure.Fonts;

public static class MiniFontReader
{
    private static readonly byte[] BmfMagic = [0x42, 0x4D, 0x46, 0x03]; // BMF\x03

    public static MiniFont Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Read(stream);
    }

    public static MiniFont Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        return Read(stream);
    }

    public static MiniFont Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        // 9-byte container header
        var formatType = reader.ReadByte();
        var bmfLength = reader.ReadUInt32();
        var textureLength = reader.ReadUInt32();

        var bmfBytes = reader.ReadBytes((int)bmfLength);
        if (bmfBytes.Length != (int)bmfLength)
        {
            throw new InvalidDataException($"Expected {bmfLength} bytes for BMF block, got {bmfBytes.Length}.");
        }

        var texturePngBytes = reader.ReadBytes((int)textureLength);
        if (texturePngBytes.Length != (int)textureLength)
        {
            throw new InvalidDataException($"Expected {textureLength} bytes for PNG texture, got {texturePngBytes.Length}.");
        }

        return ParseBmf(formatType, bmfBytes, texturePngBytes);
    }

    private static MiniFont ParseBmf(byte formatType, byte[] bmfBytes, byte[] textureBytes)
    {
        if (bmfBytes.Length < 4 ||
            bmfBytes[0] != BmfMagic[0] ||
            bmfBytes[1] != BmfMagic[1] ||
            bmfBytes[2] != BmfMagic[2] ||
            bmfBytes[3] != BmfMagic[3])
        {
            throw new InvalidDataException("Invalid BMFont magic header. Expected 'BMF\\x03'.");
        }

        using var ms = new MemoryStream(bmfBytes, writable: false);
        ms.Position = 4;
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        FontInfo? info = null;
        FontCommon? common = null;
        var pageNames = new List<string>();
        var glyphs = new List<FontGlyph>();
        var kerningPairs = new List<FontKerningPair>();

        while (ms.Position < ms.Length)
        {
            if (ms.Position + 5 > ms.Length)
            {
                break;
            }

            var blockId = reader.ReadByte();
            var blockSize = reader.ReadUInt32();
            var blockEnd = ms.Position + blockSize;

            if (blockEnd > ms.Length)
            {
                throw new InvalidDataException($"Corrupted BMFont block {blockId}: size {blockSize} exceeds buffer.");
            }

            switch (blockId)
            {
                case 1: // Info
                    info = ReadInfoBlock(reader, blockSize);
                    break;

                case 2: // Common
                    common = ReadCommonBlock(reader);
                    break;

                case 3: // Pages
                    ReadPagesBlock(reader, blockSize, pageNames);
                    break;

                case 4: // Chars
                    ReadCharsBlock(reader, blockSize, glyphs);
                    break;

                case 5: // Kerning pairs
                    ReadKerningBlock(reader, blockSize, kerningPairs);
                    break;

                default:
                    // Unknown block, skip
                    ms.Position = blockEnd;
                    break;
            }

            ms.Position = blockEnd;
        }

        info ??= new FontInfo(
            FontName: "Unknown",
            FontSize: 16,
            Bold: false,
            Italic: false,
            Unicode: true,
            Smooth: false,
            Charset: 0,
            StretchH: 100,
            Aa: 0,
            PaddingUp: 0,
            PaddingRight: 0,
            PaddingDown: 0,
            PaddingLeft: 0,
            SpacingHoriz: 0,
            SpacingVert: 0,
            Outline: 0);

        common ??= new FontCommon(
            LineHeight: 16,
            Base: 14,
            ScaleW: 256,
            ScaleH: 256,
            Pages: 1,
            Packed: false,
            AlphaChannel: 0,
            RedChannel: 0,
            GreenChannel: 0,
            BlueChannel: 0);

        return new MiniFont(
            formatType: formatType,
            info: info,
            common: common,
            pageNames: pageNames,
            glyphs: glyphs,
            kerningPairs: kerningPairs,
            texturePngBytes: textureBytes);
    }

    private static FontInfo ReadInfoBlock(BinaryReader reader, uint blockSize)
    {
        var fontSize = reader.ReadInt16();
        var bitField = reader.ReadByte();
        var charset = reader.ReadByte();
        var stretchH = reader.ReadUInt16();
        var aa = reader.ReadByte();
        var paddingUp = reader.ReadByte();
        var paddingRight = reader.ReadByte();
        var paddingDown = reader.ReadByte();
        var paddingLeft = reader.ReadByte();
        var spacingHoriz = reader.ReadByte();
        var spacingVert = reader.ReadByte();
        var outline = reader.ReadByte();

        var nameBytesCount = (int)(blockSize - 14);
        var nameBytes = reader.ReadBytes(nameBytesCount);
        var fontName = ReadNullTerminatedString(nameBytes);

        return new FontInfo(
            FontName: fontName,
            FontSize: fontSize,
            Bold: (bitField & (1 << 4)) != 0,
            Italic: (bitField & (1 << 5)) != 0,
            Unicode: (bitField & (1 << 6)) != 0,
            Smooth: (bitField & (1 << 7)) != 0,
            Charset: charset,
            StretchH: stretchH,
            Aa: aa,
            PaddingUp: paddingUp,
            PaddingRight: paddingRight,
            PaddingDown: paddingDown,
            PaddingLeft: paddingLeft,
            SpacingHoriz: spacingHoriz,
            SpacingVert: spacingVert,
            Outline: outline);
    }

    private static FontCommon ReadCommonBlock(BinaryReader reader)
    {
        var lineHeight = reader.ReadUInt16();
        var baseOffset = reader.ReadUInt16();
        var scaleW = reader.ReadUInt16();
        var scaleH = reader.ReadUInt16();
        var pages = reader.ReadUInt16();
        var bitField = reader.ReadByte();
        var alphaChnl = reader.ReadByte();
        var redChnl = reader.ReadByte();
        var greenChnl = reader.ReadByte();
        var blueChnl = reader.ReadByte();

        return new FontCommon(
            LineHeight: lineHeight,
            Base: baseOffset,
            ScaleW: scaleW,
            ScaleH: scaleH,
            Pages: pages,
            Packed: (bitField & (1 << 7)) != 0,
            AlphaChannel: alphaChnl,
            RedChannel: redChnl,
            GreenChannel: greenChnl,
            BlueChannel: blueChnl);
    }

    private static void ReadPagesBlock(BinaryReader reader, uint blockSize, List<string> pageNames)
    {
        var bytes = reader.ReadBytes((int)blockSize);
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0)
            {
                var length = i - start;
                if (length > 0)
                {
                    pageNames.Add(Encoding.UTF8.GetString(bytes, start, length));
                }
                start = i + 1;
            }
        }
    }

    private static void ReadCharsBlock(BinaryReader reader, uint blockSize, List<FontGlyph> glyphs)
    {
        var count = blockSize / 20;
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadUInt32();
            var x = reader.ReadUInt16();
            var y = reader.ReadUInt16();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            var xOffset = reader.ReadInt16();
            var yOffset = reader.ReadInt16();
            var xAdvance = reader.ReadInt16();
            var page = reader.ReadByte();
            var channel = reader.ReadByte();

            glyphs.Add(new FontGlyph(
                Id: id,
                X: x,
                Y: y,
                Width: width,
                Height: height,
                XOffset: xOffset,
                YOffset: yOffset,
                XAdvance: xAdvance,
                Page: page,
                Channel: channel));
        }
    }

    private static void ReadKerningBlock(BinaryReader reader, uint blockSize, List<FontKerningPair> kerningPairs)
    {
        var count = blockSize / 10;
        for (var i = 0; i < count; i++)
        {
            var first = reader.ReadUInt32();
            var second = reader.ReadUInt32();
            var amount = reader.ReadInt16();

            kerningPairs.Add(new FontKerningPair(
                First: first,
                Second: second,
                Amount: amount));
        }
    }

    private static string ReadNullTerminatedString(byte[] bytes)
    {
        var length = 0;
        while (length < bytes.Length && bytes[length] != 0)
        {
            length++;
        }

        return length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, 0, length);
    }
}
