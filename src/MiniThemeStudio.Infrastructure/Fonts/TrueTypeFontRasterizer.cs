using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MiniThemeStudio.Core.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Fonts;

public sealed class TrueTypeFontRasterizer : ITrueTypeFontRasterizer
{
    private static readonly char[] DefaultCharacterSet =
    [
        // ASCII printable
        ' ', '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':', ';', '<', '=', '>', '?',
        '@', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O',
        'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '[', '\\', ']', '^', '_',
        '`', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o',
        'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', '{', '|', '}', '~',

        // Essential European accents & symbols (prevention of C8)
        '¡', '¿', '«', '»', '©', '®', '°',
        'á', 'à', 'â', 'ã', 'ä', 'é', 'è', 'ê', 'ë', 'í', 'ì', 'î', 'ï',
        'ó', 'ò', 'ô', 'õ', 'ö', 'ú', 'ù', 'û', 'ü', 'ç', 'ñ',
        'Á', 'À', 'Â', 'Ã', 'Ä', 'É', 'È', 'Ê', 'Ë', 'Í', 'Ì', 'Î', 'Ï',
        'Ó', 'Ò', 'Ô', 'Õ', 'Ö', 'Ú', 'Ù', 'Û', 'Ü', 'Ç', 'Ñ'
    ];

    public MiniFont Rasterize(string fontOrZipPath, TrueTypeRasterOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontOrZipPath);

        if (fontOrZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(fontOrZipPath);
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                throw new InvalidDataException($"No .ttf or .otf font found inside zip archive: {fontOrZipPath}");
            }

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            var fontName = Path.GetFileNameWithoutExtension(entry.Name);
            return Rasterize(ms, options, fontName);
        }

        using var fileStream = File.OpenRead(fontOrZipPath);
        var baseName = Path.GetFileNameWithoutExtension(fontOrZipPath);
        return Rasterize(fileStream, options, baseName);
    }

    public MiniFont Rasterize(Stream fontStream, TrueTypeRasterOptions options, string? fontName = null)
    {
        ArgumentNullException.ThrowIfNull(fontStream);
        ArgumentNullException.ThrowIfNull(options);

        using var typeface = SKTypeface.FromStream(fontStream);
        if (typeface is null)
        {
            throw new InvalidDataException("Invalid TrueType or OpenType font stream.");
        }

        var effectiveFontName = !string.IsNullOrWhiteSpace(fontName)
            ? fontName
            : (!string.IsNullOrWhiteSpace(typeface.FamilyName) ? typeface.FamilyName : "CustomFont");

        var fontSize = Math.Clamp(options.FontSize, 6, 96);
        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize,
            IsAntialias = options.AntiAlias,
            Color = SKColors.White,
        };

        paint.GetFontMetrics(out var metrics);
        var lineHeight = (int)Math.Ceiling(metrics.Descent - metrics.Ascent + metrics.Leading);
        var baseOffset = (int)Math.Ceiling(-metrics.Ascent);
        if (lineHeight <= 0)
        {
            lineHeight = fontSize + 4;
        }

        var charset = options.CharacterSet ?? DefaultCharacterSet;
        var pendingGlyphs = new List<(char Ch, char DrawChar, SKRect Bounds, float Advance)>();

        foreach (var c in charset)
        {
            var glyphId = typeface.GetGlyph(c);
            var drawChar = c;

            // Fallback for uppercase-only pixel fonts
            if (glyphId == 0 && char.IsLower(c))
            {
                var upper = char.ToUpperInvariant(c);
                if (typeface.GetGlyph(upper) != 0)
                {
                    glyphId = typeface.GetGlyph(upper);
                    drawChar = upper;
                }
            }

            if (c != ' ' && glyphId == 0)
            {
                continue; // Glyph not available in font
            }

            var bounds = new SKRect();
            var advance = paint.MeasureText(drawChar.ToString(), ref bounds);

            if (c == ' ')
            {
                advance = advance > 0 ? advance : fontSize / 2f;
                pendingGlyphs.Add((c, ' ', SKRect.Empty, advance));
                continue;
            }

            if (bounds.Width > 0 && bounds.Height > 0)
            {
                pendingGlyphs.Add((c, drawChar, bounds, advance));
            }
        }

        // Shelf Packing into Atlas
        var atlasWidth = options.AtlasWidth > 0 ? options.AtlasWidth : 512;
        const int padding = 2;
        var curX = padding;
        var curY = padding;
        var rowHeight = 0;

        var packedGlyphs = new List<(char Ch, char DrawChar, SKRect Bounds, float Advance, int X, int Y, int W, int H)>();

        foreach (var item in pendingGlyphs)
        {
            if (item.Ch == ' ')
            {
                continue;
            }

            var w = (int)Math.Ceiling(item.Bounds.Width);
            var h = (int)Math.Ceiling(item.Bounds.Height);

            if (curX + w + padding > atlasWidth)
            {
                curX = padding;
                curY += rowHeight + padding;
                rowHeight = 0;
            }

            packedGlyphs.Add((item.Ch, item.DrawChar, item.Bounds, item.Advance, curX, curY, w, h));
            rowHeight = Math.Max(rowHeight, h);
            curX += w + padding;
        }

        var totalNeededHeight = curY + rowHeight + padding;
        var atlasHeight = NextPowerOfTwo(totalNeededHeight);

        // Render onto Atlas Surface
        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var finalGlyphs = new List<FontGlyph>();

        // Add space glyph
        var spaceItem = pendingGlyphs.FirstOrDefault(p => p.Ch == ' ');
        var spaceAdv = spaceItem.Advance > 0 ? (int)Math.Round(spaceItem.Advance) : fontSize / 2;
        finalGlyphs.Add(new FontGlyph(
            Id: ' ',
            X: 0,
            Y: 0,
            Width: 0,
            Height: 0,
            XOffset: 0,
            YOffset: 0,
            XAdvance: spaceAdv,
            Page: 0,
            Channel: 15));

        foreach (var p in packedGlyphs)
        {
            // Draw character exactly into its packed cell
            var drawX = p.X - p.Bounds.Left;
            var drawY = p.Y - p.Bounds.Top;
            canvas.DrawText(p.DrawChar.ToString(), drawX, drawY, paint);

            var xOff = (int)Math.Floor(p.Bounds.Left);
            var yOff = baseOffset + (int)Math.Floor(p.Bounds.Top);

            finalGlyphs.Add(new FontGlyph(
                Id: p.Ch,
                X: p.X,
                Y: p.Y,
                Width: p.W,
                Height: p.H,
                XOffset: xOff,
                YOffset: yOff,
                XAdvance: (int)Math.Round(p.Advance),
                Page: 0,
                Channel: 15));
        }

        using var image = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        var texturePngBytes = pngData.ToArray();

        var fontInfo = new FontInfo(
            FontName: effectiveFontName,
            FontSize: (short)fontSize,
            Bold: false,
            Italic: false,
            Unicode: true,
            Smooth: options.AntiAlias,
            Charset: 0,
            StretchH: 100,
            Aa: (byte)(options.AntiAlias ? 1 : 0),
            PaddingUp: 0,
            PaddingRight: 0,
            PaddingDown: 0,
            PaddingLeft: 0,
            SpacingHoriz: 0,
            SpacingVert: 0,
            Outline: 0);

        var fontCommon = new FontCommon(
            LineHeight: lineHeight,
            Base: baseOffset,
            ScaleW: atlasWidth,
            ScaleH: atlasHeight,
            Pages: 1,
            Packed: false,
            AlphaChannel: 0,
            RedChannel: 0,
            GreenChannel: 0,
            BlueChannel: 0);

        return new MiniFont(
            formatType: 1,
            info: fontInfo,
            common: fontCommon,
            pageNames: [$"{effectiveFontName}_0.png"],
            glyphs: finalGlyphs,
            kerningPairs: [],
            texturePngBytes: texturePngBytes);
    }

    private static int NextPowerOfTwo(int value)
    {
        var power = 64;
        while (power < value && power < 2048)
        {
            power *= 2;
        }

        return Math.Max(power, value);
    }
}
