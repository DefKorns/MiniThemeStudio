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
    // ── Public API ───────────────────────────────────────────────────────────

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
                throw new InvalidDataException($"No .ttf or .otf font found inside zip archive: {fontOrZipPath}");

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            return Rasterize(ms, options, Path.GetFileNameWithoutExtension(entry.Name));
        }

        using var fileStream = File.OpenRead(fontOrZipPath);
        return Rasterize(fileStream, options, Path.GetFileNameWithoutExtension(fontOrZipPath));
    }

    public MiniFont Rasterize(Stream fontStream, TrueTypeRasterOptions options, string? fontName = null)
    {
        ArgumentNullException.ThrowIfNull(fontStream);
        ArgumentNullException.ThrowIfNull(options);

        using var typeface = SKTypeface.FromStream(fontStream);
        if (typeface is null)
            throw new InvalidDataException("Invalid TrueType or OpenType font stream.");

        var effectiveFontName = !string.IsNullOrWhiteSpace(fontName) ? fontName
            : !string.IsNullOrWhiteSpace(typeface.FamilyName) ? typeface.FamilyName
            : "CustomFont";

        var fontSize = Math.Clamp(options.FontSize, 6, 96);

        // ── Script fallback typeface (for extra-script glyphs) ────────────────
        // Priority: user-specified family → system auto-match per character
        SKTypeface? scriptFallback = null;
        if (!string.IsNullOrWhiteSpace(options.ScriptFallbackFamily))
        {
            scriptFallback = SKTypeface.FromFamilyName(options.ScriptFallbackFamily);
        }

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
        if (lineHeight <= 0) lineHeight = fontSize + 4;

        // ── Build full character set ──────────────────────────────────────────
        var charset = BuildCharacterSet(options);

        // ── MiniFont bitmap fallback (existing mechanism) ─────────────────────
        using var fallbackTexture = options.FallbackFont?.TexturePngBytes is { Length: > 0 }
            ? SKBitmap.Decode(options.FallbackFont.TexturePngBytes)
            : null;

        // ── Resolve glyphs ───────────────────────────────────────────────────
        var pendingGlyphs = new List<(char Ch, char DrawChar, SKRect Bounds, float Advance,
            bool IsMiniFallback, FontGlyph? MiniGlyph,
            bool IsScriptFallback, SKTypeface? ScriptTf)>();

        foreach (var c in charset)
        {
            // Space — always from the custom font metrics
            if (c == ' ')
            {
                var bounds = new SKRect();
                var advance = paint.MeasureText(" ", ref bounds);
                advance = advance > 0 ? advance : fontSize / 2f;
                pendingGlyphs.Add((c, ' ', SKRect.Empty, advance, false, null, false, null));
                continue;
            }

            // 1. Try custom typeface
            var glyphId  = typeface.GetGlyph(c);
            var drawChar = c;

            // Uppercase fallback for pixel fonts that only have caps
            if (glyphId == 0 && char.IsLower(c))
            {
                var upper = char.ToUpperInvariant(c);
                if (typeface.GetGlyph(upper) != 0)
                {
                    glyphId  = typeface.GetGlyph(upper);
                    drawChar = upper;
                }
            }

            if (glyphId != 0)
            {
                var bounds  = new SKRect();
                var advance = paint.MeasureText(drawChar.ToString(), ref bounds);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    pendingGlyphs.Add((c, drawChar, bounds, advance, false, null, false, null));
                    continue;
                }
            }

            // 2. Try MiniFont bitmap fallback (for pre-generated console glyphs)
            if (options.FallbackFont?.GetGlyph(c) is { } miniGlyph && fallbackTexture is not null)
            {
                var fb = new SKRect(0, 0, miniGlyph.Width, miniGlyph.Height);
                pendingGlyphs.Add((c, c, fb, miniGlyph.XAdvance, true, miniGlyph, false, null));
                continue;
            }

            // 3. Try script fallback typeface (Yu Gothic UI / Noto Sans / auto)
            var scriptTf = ResolveScriptTypeface(c, scriptFallback);
            if (scriptTf is not null)
            {
                using var scriptPaint = new SKPaint
                {
                    Typeface = scriptTf,
                    TextSize = fontSize,
                    IsAntialias = options.AntiAlias,
                    Color = SKColors.White,
                };
                var bounds  = new SKRect();
                var advance = scriptPaint.MeasureText(c.ToString(), ref bounds);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    pendingGlyphs.Add((c, c, bounds, advance, false, null, true, scriptTf));
                    continue;
                }
            }

            // Glyph not found anywhere — skip silently
        }

        // ── Shelf packing into atlas ─────────────────────────────────────────
        var atlasWidth = options.AtlasWidth > 0 ? options.AtlasWidth : 512;
        const int padding = 2;
        var curX = padding;
        var curY = padding;
        var rowHeight = 0;

        var packedGlyphs = new List<(char Ch, char DrawChar, SKRect Bounds, float Advance,
            bool IsMiniF, FontGlyph? MiniG, bool IsScriptF, SKTypeface? ScriptTf,
            int X, int Y, int W, int H)>();

        foreach (var item in pendingGlyphs)
        {
            if (item.Ch == ' ') continue;

            var w = (int)Math.Ceiling(item.Bounds.Width);
            var h = (int)Math.Ceiling(item.Bounds.Height);

            if (curX + w + padding > atlasWidth)
            {
                curX = padding;
                curY += rowHeight + padding;
                rowHeight = 0;
            }

            packedGlyphs.Add((item.Ch, item.DrawChar, item.Bounds, item.Advance,
                item.IsMiniFallback, item.MiniGlyph, item.IsScriptFallback, item.ScriptTf,
                curX, curY, w, h));

            rowHeight = Math.Max(rowHeight, h);
            curX += w + padding;
        }

        var atlasHeight = NextPowerOfTwo(curY + rowHeight + padding);

        // ── Render onto atlas surface ────────────────────────────────────────
        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var finalGlyphs = new List<FontGlyph>();

        // Space glyph (zero-size, XAdvance only)
        var spaceItem = pendingGlyphs.FirstOrDefault(p => p.Ch == ' ');
        var spaceAdv  = spaceItem.Advance > 0 ? (int)Math.Round(spaceItem.Advance) : fontSize / 2;
        finalGlyphs.Add(new FontGlyph(Id: ' ', X: 0, Y: 0, Width: 0, Height: 0,
            XOffset: 0, YOffset: 0, XAdvance: spaceAdv, Page: 0, Channel: 15));

        foreach (var p in packedGlyphs)
        {
            if (p.IsMiniF && p.MiniG is not null && fallbackTexture is not null)
            {
                // Blit from MiniFont bitmap fallback
                if (p.W > 0 && p.H > 0)
                {
                    var src = new SKRect(p.MiniG.X, p.MiniG.Y, p.MiniG.X + p.W, p.MiniG.Y + p.H);
                    canvas.DrawBitmap(fallbackTexture, src, new SKRect(p.X, p.Y, p.X + p.W, p.Y + p.H));
                }
                finalGlyphs.Add(new FontGlyph(p.Ch, p.X, p.Y, p.W, p.H,
                    p.MiniG.XOffset, p.MiniG.YOffset, p.MiniG.XAdvance, 0, 15));
            }
            else if (p.IsScriptF && p.ScriptTf is not null)
            {
                // Draw using the script fallback typeface
                using var scriptPaint = new SKPaint
                {
                    Typeface = p.ScriptTf,
                    TextSize = fontSize,
                    IsAntialias = options.AntiAlias,
                    Color = SKColors.White,
                };
                scriptPaint.GetFontMetrics(out var sm);
                var sBase = (int)Math.Ceiling(-sm.Ascent);
                canvas.DrawText(p.DrawChar.ToString(), p.X - p.Bounds.Left, p.Y - p.Bounds.Top, scriptPaint);

                finalGlyphs.Add(new FontGlyph(p.Ch, p.X, p.Y, p.W, p.H,
                    (int)Math.Floor(p.Bounds.Left),
                    sBase + (int)Math.Floor(p.Bounds.Top),
                    (int)Math.Round(p.Advance), 0, 15));
            }
            else
            {
                // Draw using custom typeface
                canvas.DrawText(p.DrawChar.ToString(), p.X - p.Bounds.Left, p.Y - p.Bounds.Top, paint);

                finalGlyphs.Add(new FontGlyph(p.Ch, p.X, p.Y, p.W, p.H,
                    (int)Math.Floor(p.Bounds.Left),
                    baseOffset + (int)Math.Floor(p.Bounds.Top),
                    (int)Math.Round(p.Advance), 0, 15));
            }
        }

        using var image   = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MiniFont(
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
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<char> BuildCharacterSet(TrueTypeRasterOptions options) =>
        options.CharacterSet is { Count: > 0 } ? [.. options.CharacterSet] : FontCharsets.Build(options.ExtraScripts);

    /// <summary>
    /// Returns a typeface that contains a glyph for <paramref name="c"/>,
    /// trying the user-specified fallback first, then the system font manager.
    /// Returns null if no typeface is found.
    /// </summary>
    private static SKTypeface? ResolveScriptTypeface(char c, SKTypeface? preferredFallback)
    {
        if (preferredFallback is not null && preferredFallback.GetGlyph(c) != 0)
            return preferredFallback;

        // Auto-match from system fonts (Noto Sans, Yu Gothic, etc.)
        var matched = SKFontManager.Default.MatchCharacter(c);
        if (matched is not null && matched.GetGlyph(c) != 0)
            return matched;

        return null;
    }

    private static int NextPowerOfTwo(int value)
    {
        var power = 64;
        while (power < value && power < 4096) power *= 2;
        return Math.Max(power, value);
    }
}
