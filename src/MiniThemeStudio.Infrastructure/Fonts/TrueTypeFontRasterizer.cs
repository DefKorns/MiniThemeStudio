using System.IO.Compression;
using MiniThemeStudio.Core.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Fonts;

public sealed class TrueTypeFontRasterizer : ITrueTypeFontRasterizer
{
    // ── Public API ───────────────────────────────────────────────────────────

    public MiniFont Rasterize(string fontOrZipPath, TrueTypeRasterOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontOrZipPath);

        if (fontOrZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(fontOrZipPath);
            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"No .ttf or .otf font found inside zip archive: {fontOrZipPath}");

            using Stream entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            return Rasterize(ms, options, Path.GetFileNameWithoutExtension(entry.Name));
        }

        using FileStream fileStream = File.OpenRead(fontOrZipPath);
        return Rasterize(fileStream, options, Path.GetFileNameWithoutExtension(fontOrZipPath));
    }

    public MiniFont Rasterize(Stream fontStream, TrueTypeRasterOptions options, string? fontName = null)
    {
        ArgumentNullException.ThrowIfNull(fontStream);
        ArgumentNullException.ThrowIfNull(options);

        using SKTypeface typeface = SKTypeface.FromStream(fontStream)
            ?? throw new InvalidDataException("Invalid TrueType or OpenType font stream.");

        string effectiveFontName = !string.IsNullOrWhiteSpace(fontName) ? fontName
            : !string.IsNullOrWhiteSpace(typeface.FamilyName) ? typeface.FamilyName
            : "CustomFont";

        int fontSize = Math.Clamp(options.FontSize, 6, 96);

        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize,
            IsAntialias = options.AntiAlias,
            Color = SKColors.White,
        };

        paint.GetFontMetrics(out SKFontMetrics metrics);
        int lineHeight = (int)Math.Ceiling(metrics.Descent - metrics.Ascent + metrics.Leading);
        int baseOffset = (int)Math.Ceiling(-metrics.Ascent);
        if (lineHeight <= 0)
        {
            lineHeight = fontSize + 4;
        }

        // ── Build full character set ──────────────────────────────────────────
        IReadOnlyList<char> charset = BuildCharacterSet(options);

        // ── Resolve glyphs ───────────────────────────────────────────────────
        var pendingGlyphs = new List<(char Ch, char DrawChar, SKRect Bounds, float Advance)>();

        foreach (char c in charset)
        {
            // Space — always from the custom font metrics
            if (c == ' ')
            {
                var bounds = new SKRect();
                float advance = paint.MeasureText(" ", ref bounds);
                advance = advance > 0 ? advance : fontSize / 2f;
                pendingGlyphs.Add((c, ' ', SKRect.Empty, advance));
                continue;
            }

            // 1. Try custom typeface
            ushort glyphId = typeface.GetGlyph(c);
            char drawChar = c;

            // Uppercase fallback for pixel fonts that only have caps
            if (glyphId == 0 && char.IsLower(c))
            {
                char upper = char.ToUpperInvariant(c);
                if (typeface.GetGlyph(upper) != 0)
                {
                    glyphId = typeface.GetGlyph(upper);
                    drawChar = upper;
                }
            }

            if (glyphId != 0)
            {
                var bounds = new SKRect();
                float advance = paint.MeasureText(drawChar.ToString(), ref bounds);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    pendingGlyphs.Add((c, drawChar, bounds, advance));
                }
            }
        }

        // ── Shelf packing into atlas ─────────────────────────────────────────
        int atlasWidth = options.AtlasWidth > 0 ? options.AtlasWidth : 512;
        const int padding = 2;
        int curX = padding;
        int curY = padding;
        int rowHeight = 0;

        var packedGlyphs = new List<(char Ch, char DrawChar, SKRect Bounds, float Advance, int X, int Y, int W, int H)>();

        foreach ((char Ch, char DrawChar, SKRect Bounds, float Advance) item in pendingGlyphs)
        {
            if (item.Ch == ' ')
            {
                continue;
            }

            int w = (int)Math.Ceiling(item.Bounds.Width);
            int h = (int)Math.Ceiling(item.Bounds.Height);

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

        int atlasHeight = NextPowerOfTwo(curY + rowHeight + padding);

        // ── Render onto atlas surface ────────────────────────────────────────
        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var finalGlyphs = new List<FontGlyph>();

        // Space glyph (zero-size, XAdvance only)
        (char Ch, char DrawChar, SKRect Bounds, float Advance) spaceItem = pendingGlyphs.FirstOrDefault(p => p.Ch == ' ');
        int spaceAdv = spaceItem.Advance > 0 ? (int)Math.Round(spaceItem.Advance) : fontSize / 2;
        finalGlyphs.Add(new FontGlyph(Id: ' ', X: 0, Y: 0, Width: 0, Height: 0,
            XOffset: 0, YOffset: 0, XAdvance: spaceAdv, Page: 0, Channel: 15));

        foreach ((char Ch, char DrawChar, SKRect Bounds, float Advance, int X, int Y, int W, int H) p in packedGlyphs)
        {
            canvas.DrawText(p.DrawChar.ToString(), p.X - p.Bounds.Left, p.Y - p.Bounds.Top, paint);

            finalGlyphs.Add(new FontGlyph(p.Ch, p.X, p.Y, p.W, p.H,
                (int)Math.Floor(p.Bounds.Left),
                baseOffset + (int)Math.Floor(p.Bounds.Top),
                (int)Math.Round(p.Advance), 0, 15));
        }

        using SKImage image = surface.Snapshot();
        using SKData pngData = image.Encode(SKEncodedImageFormat.Png, 100);

        var rasterized = new MiniFont(
            formatType: 1,
            info: new FontInfo(effectiveFontName, (short)fontSize,
                Bold: false, Italic: false, Unicode: true, Smooth: options.AntiAlias,
                Charset: 0, StretchH: 100, Aa: (byte)(options.AntiAlias ? 1 : 0),
                PaddingUp: 0, PaddingRight: 0, PaddingDown: 0, PaddingLeft: 0,
                SpacingHoriz: 0, SpacingVert: 0, Outline: 0),
            common: new FontCommon(lineHeight, baseOffset, atlasWidth, atlasHeight,
                Pages: 1, Packed: false, AlphaChannel: 0, RedChannel: 0, GreenChannel: 0, BlueChannel: 0),
            pageNames: [$"{effectiveFontName}_0.png"],
            glyphs: finalGlyphs,
            kerningPairs: [],
            texturePngBytes: pngData.ToArray());

        // whatever the font lacks comes from the complete reference of its role
        return MiniFontCompleter.Complete(rasterized, options.ExtraScripts, options.FillFontPath);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<char> BuildCharacterSet(TrueTypeRasterOptions options) =>
        options.CharacterSet is { Count: > 0 } ? [.. options.CharacterSet] : FontCharsets.Build(options.ExtraScripts);

    private static int NextPowerOfTwo(int value)
    {
        int power = 64;
        while (power < value && power < 4096)
        {
            power *= 2;
        }

        return Math.Max(power, value);
    }
}
