using System.Collections.Concurrent;
using MiniThemeStudio.Core.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Fonts;

/// <summary>
/// Gives a font every character of its charsets: the missing ones are drawn from the system's Yu Gothic UI when
/// installed (Font Remount's typeface), else from the embedded Noto Sans (Latin, Greek, Cyrillic) or Noto Sans JP;
/// sized so their capitals match the font's, set on its baseline, in the weight closest to the font's own strokes.
/// Glyphs of scripts not asked for are dropped. The atlas is repacked.
/// </summary>
public static class MiniFontCompleter
{
    private const int Padding = 2;
    private const float MeasureSize = 100f;

    private const int DefaultWeight = 400;
    private static readonly int[] Weights = [100, 200, 400, 700];
    // Font Remount's title.fnt is Yu Gothic UI; it can't ship with the app, so it's used only where the system has it:
    // Windows names it Yu Gothic UI, macOS ships its own as YuGothic
    private static readonly string[] SystemFamilies = ["Yu Gothic UI", "Yu Gothic", "YuGothic"];
    private static readonly ConcurrentDictionary<int, (SKTypeface Latin, SKTypeface Japanese)> NotoFaces = new();
    private static readonly ConcurrentDictionary<int, SKTypeface?> SystemFaces = new();
    private static readonly ConcurrentDictionary<string, SKTypeface?> UserFaces = new();

    /// <param name="font">the console font to complete</param>
    /// <param name="scripts">the scripts to add besides the missing Latin characters</param>
    /// <param name="fillFontPath">the user's own font file, tried before the system's and the built-in ones</param>
    /// <param name="redrawText">draws every character of the sets again, not only the missing ones, so the text is
    /// one typeface: Yu Gothic UI, the letter of Font Remount's title.fnt, at the font's own size and weight. The
    /// glyphs outside the sets (the button icons of some console fonts) are kept.</param>
    public static MiniFont Complete(MiniFont font, ExtraScripts scripts, string? fillFontPath = null, bool redrawText = false)
    {
        ArgumentNullException.ThrowIfNull(font);

        var kept = font.Glyphs.Where(g => IsWanted(g.Id, scripts)).ToList();
        if (redrawText)
        {
            HashSet<uint> redrawn = FontCharsets.Build(scripts).Where(c => c != ' ').Select(c => (uint)c).ToHashSet();
            kept = kept.Where(glyph => !redrawn.Contains(glyph.Id)).ToList();
        }

        var have = kept.Select(g => g.Id).ToHashSet();
        var missing = FontCharsets.Build(scripts).Where(c => c != ' ' && !have.Contains(c)).ToList();
        if (missing.Count == 0 && kept.Count == font.Glyphs.Count)
        {
            return font;
        }

        (float textSize, int baseline) = Fit(font);
        using var decoded = SKBitmap.Decode(font.TexturePngBytes);
        using SKBitmap fontAtlas = decoded.ColorType == SKColorType.Rgba8888 || decoded.ColorType == SKColorType.Bgra8888
            ? decoded.Copy()
            : decoded.Copy(SKColorType.Rgba8888);
        int? latinWeight = MatchWeight(font, fontAtlas, textSize, japanese: false);
        int? japaneseWeight = MatchWeight(font, fontAtlas, textSize, japanese: true);
        SKTypeface latin = Face(latinWeight ?? japaneseWeight ?? DefaultWeight).Latin;
        SKTypeface japanese = Face(japaneseWeight ?? latinWeight ?? DefaultWeight).Japanese;
        bool smooth = IsSmooth(font, fontAtlas);
        using SKPaint? userPaint = UserFace(fillFontPath) is { } user ? Paint(user, textSize, smooth) : null;
        using SKPaint latinPaint = Paint(latin, textSize, smooth);
        using SKPaint japanesePaint = Paint(japanese, textSize, smooth);
        using SKPaint notoLatinPaint = Paint(NotoFace(latinWeight ?? japaneseWeight ?? DefaultWeight).Latin, textSize, smooth);
        using SKPaint notoJapanesePaint = Paint(NotoFace(japaneseWeight ?? latinWeight ?? DefaultWeight).Japanese, textSize, smooth);

        var drawn = new List<(FontGlyph Glyph, SKBitmap? Bitmap)>();
        foreach (char c in missing)
        {
            SKPaint? paint = new[] { userPaint, latinPaint, japanesePaint, notoLatinPaint, notoJapanesePaint }.FirstOrDefault(p => p?.Typeface.ContainsGlyph(c) == true);
            if (paint is null)
            {
                continue;
            }

            SKRect bounds = InkBounds(paint, c);
            float advance = paint.MeasureText(c.ToString());
            int width = (int)Math.Ceiling(bounds.Width);
            int height = (int)Math.Ceiling(bounds.Height);
            SKBitmap? bitmap = null;
            if (width > 0 && height > 0)
            {
                bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawText(c.ToString(), -bounds.Left, -bounds.Top, paint);
            }

            drawn.Add((new FontGlyph(c, 0, 0, bitmap is null ? 0 : width, bitmap is null ? 0 : height,
                (int)Math.Floor(bounds.Left), baseline + (int)Math.Floor(bounds.Top), (int)Math.Round(advance), 0, 15), bitmap));
        }

        try
        {
            return Repack(font, fontAtlas, kept, drawn);
        }
        finally
        {
            foreach ((FontGlyph _, SKBitmap? bitmap) in drawn)
            {
                bitmap?.Dispose();
            }
        }
    }

    public static bool IsWanted(uint id, ExtraScripts scripts)
    {
        if (id > char.MaxValue)
        {
            return false;
        }

        char c = (char)id;
        return (scripts.HasFlag(ExtraScripts.Japanese) || !IsJapanese(c))
            && (scripts.HasFlag(ExtraScripts.Cyrillic) || !IsCyrillic(c))
            && (scripts.HasFlag(ExtraScripts.Greek) || !IsGreek(c));
    }

    public static bool IsJapanese(char c) =>
        c is (>= '　' and <= 'ヿ') or (>= 'ㇰ' and <= 'ㇿ') or (>= '一' and <= '鿿')
            or (>= '⺀' and <= '⿕') or (>= '＀' and <= '￯');

    public static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    public static bool IsGreek(char c) => c is >= 'Ͱ' and <= 'Ͽ';

    // the size that gives Noto the font's capital height, and the font's baseline (where its capitals end);
    // a font without enough capitals or digits falls back to its line height and base
    private static (float TextSize, int Baseline) Fit(MiniFont font)
    {
        using SKPaint measure = Paint(Face(DefaultWeight).Latin, MeasureSize, false);
        var ratios = new List<double>();
        var bottoms = new List<double>();
        foreach (FontGlyph? glyph in font.Glyphs.Where(g => g.Height > 0 && g.Id is (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
        {
            SKRect bounds = InkBounds(measure, (char)glyph.Id);
            if (bounds.Height > 0)
            {
                ratios.Add(glyph.Height / bounds.Height);
                bottoms.Add(glyph.YOffset + glyph.Height);
            }
        }

        if (ratios.Count >= 3)
        {
            return ((float)(MeasureSize * Median(ratios)), (int)Math.Round(Median(bottoms)));
        }

        measure.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        return (lineHeight > 0 ? MeasureSize * font.Common.LineHeight / lineHeight : font.Common.LineHeight, font.Common.Base);
    }

    // the weight whose strokes cover as much of the glyph box as the font's capitals and digits (or its kanji) do;
    // null when the font has too few of them to tell
    private static int? MatchWeight(MiniFont font, SKBitmap atlas, float textSize, bool japanese)
    {
        var samples = font.Glyphs
            .Where(g => g.Width > 0 && g.Height > 0 && g.Id <= char.MaxValue
                && (japanese ? g.Id is >= 0x4E00 and <= 0x9FFF : g.Id is (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            .Take(12)
            .ToList();
        if (samples.Count < 3)
        {
            return null;
        }

        double target = Median(samples.Select(g => Coverage(atlas, new SKRectI(g.X, g.Y, g.X + g.Width, g.Y + g.Height))).ToList());
        return Weights.MinBy(weight =>
        {
            SKTypeface face = japanese ? Face(weight).Japanese : Face(weight).Latin;
            using SKPaint paint = Paint(face, textSize, true);
            var coverages = samples.Select(g => RenderedCoverage(paint, (char)g.Id)).Where(c => c > 0).ToList();
            return coverages.Count == 0 ? double.MaxValue : Math.Abs(Median(coverages) - target);
        });
    }

    // the header's smooth flag is often wrong: the font is smooth when its glyphs' edges are partly transparent
    private static bool IsSmooth(MiniFont font, SKBitmap atlas)
    {
        ReadOnlySpan<byte> pixels = atlas.GetPixelSpan();
        long inked = 0, partial = 0;
        foreach (FontGlyph? glyph in font.Glyphs.Where(g => g.Width > 0 && g.Height > 0).Take(40))
        {
            // a glyph outside the texture (the 1 x 1 placeholder fonts of some UIs) has no pixels to tell
            SKRectI box = Inside(atlas, new SKRectI(glyph.X, glyph.Y, glyph.X + glyph.Width, glyph.Y + glyph.Height));
            for (int y = box.Top; y < box.Bottom; y++)
            {
                for (int x = box.Left; x < box.Right; x++)
                {
                    byte alpha = pixels[y * atlas.RowBytes + x * 4 + 3];
                    inked += alpha > 0 ? 1 : 0;
                    partial += alpha is > 0 and < 255 ? 1 : 0;
                }
            }
        }

        return inked == 0 ? font.Info.Smooth : partial > inked / 5;
    }

    // the part of the box that lies on the bitmap
    private static SKRectI Inside(SKBitmap bitmap, SKRectI box) => new(
        Math.Clamp(box.Left, 0, bitmap.Width),
        Math.Clamp(box.Top, 0, bitmap.Height),
        Math.Clamp(box.Right, 0, bitmap.Width),
        Math.Clamp(box.Bottom, 0, bitmap.Height));

    private static double RenderedCoverage(SKPaint paint, char c)
    {
        SKRect bounds = InkBounds(paint, c);
        int width = (int)Math.Ceiling(bounds.Width);
        int height = (int)Math.Ceiling(bounds.Height);
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(c.ToString(), -bounds.Left, -bounds.Top, paint);
        return Coverage(bitmap, new SKRectI(0, 0, width, height));
    }

    // the share of the box the glyph's ink covers; alpha is the 4th byte in both RGBA and BGRA
    private static double Coverage(SKBitmap bitmap, SKRectI box)
    {
        box = Inside(bitmap, box);
        if (box.Width <= 0 || box.Height <= 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        long alpha = 0;
        for (int y = box.Top; y < box.Bottom; y++)
        {
            int row = y * rowBytes;
            for (int x = box.Left; x < box.Right; x++)
            {
                alpha += pixels[row + x * 4 + 3];
            }
        }

        return alpha / (255.0 * box.Width * box.Height);
    }

    private static MiniFont Repack(MiniFont font, SKBitmap fontAtlas, List<FontGlyph> kept, List<(FontGlyph Glyph, SKBitmap? Bitmap)> drawn)
    {
        var pieces = kept.Select(g => (Glyph: g, Bitmap: (SKBitmap?)null, Source: new SKRectI(g.X, g.Y, g.X + g.Width, g.Y + g.Height)))
            .Concat(drawn.Select(d => (d.Glyph, d.Bitmap, Source: new SKRectI(0, 0, d.Glyph.Width, d.Glyph.Height))))
            .OrderBy(p => p.Glyph.Id)
            .ToList();

        // the narrowest power of two that keeps the atlas no taller than it is wide: a font that started from a
        // placeholder 1 x 1 texture would otherwise become a strip thousands of pixels tall
        int atlasWidth = NextPowerOfTwo(Math.Max(font.Common.ScaleW, 64));
        List<(FontGlyph Glyph, SKBitmap? Bitmap, SKRectI Source)> placed;
        int used;
        while (true)
        {
            (placed, used) = Shelve(pieces, atlasWidth);
            if (used <= atlasWidth || atlasWidth >= 4096)
            {
                break;
            }

            atlasWidth *= 2;
        }

        int atlasHeight = NextPowerOfTwo(used);
        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        // DrawBitmap turns the whole bitmap into an image on every call; the atlas is converted once
        using var atlasImage = SKImage.FromBitmap(fontAtlas);
        foreach ((FontGlyph? glyph, SKBitmap? bitmap, SKRectI source) in placed)
        {
            if (glyph.Width <= 0 || glyph.Height <= 0)
            {
                continue;
            }

            var target = new SKRect(glyph.X, glyph.Y, glyph.X + glyph.Width, glyph.Y + glyph.Height);
            if (bitmap is null)
            {
                canvas.DrawImage(atlasImage, source, target);
            }
            else
            {
                canvas.DrawBitmap(bitmap, source, target);
            }
        }

        using SKImage snapshot = surface.Snapshot();
        using SKData png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return new MiniFont(
            formatType: font.FormatType,
            info: font.Info,
            common: font.Common with { ScaleW = atlasWidth, ScaleH = atlasHeight, Pages = 1 },
            pageNames: font.PageNames.Take(1).ToList(),
            glyphs: placed.Select(p => p.Glyph).ToList(),
            kerningPairs: font.KerningPairs,
            texturePngBytes: png.ToArray());
    }

    // the outline's own box: the bounds MeasureText gives grow a pixel on every side on macOS (room CoreText keeps
    // for antialiasing), which would make the filled glyphs taller there than on Windows and Linux
    private static SKRect InkBounds(SKPaint paint, char c)
    {
        using SKPath path = paint.GetTextPath(c.ToString(), 0, 0);
        return path.TightBounds;
    }

    private static SKPaint Paint(SKTypeface typeface, float size, bool smooth) =>
        new() { Typeface = typeface, TextSize = size, IsAntialias = smooth, Color = SKColors.White };

    // the system's Yu Gothic UI when installed, else the embedded Noto
    private static (SKTypeface Latin, SKTypeface Japanese) Face(int weight) =>
        SystemFace(weight) is { } system ? (system, system) : NotoFace(weight);

    private static (SKTypeface Latin, SKTypeface Japanese) NotoFace(int weight) =>
        NotoFaces.GetOrAdd(weight, w => (LoadTypeface($"NotoSans-W{w}.ttf"), LoadTypeface($"NotoSansJP-W{w}.ttf")));

    private static SKTypeface? UserFace(string? path) =>
        string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? null
            : UserFaces.GetOrAdd($"{path}|{File.GetLastWriteTimeUtc(path).Ticks}", _ => SKTypeface.FromFile(path));

    private static SKTypeface? SystemFace(int weight) =>
        SystemFaces.GetOrAdd(weight, w => SystemFamilies
            .Select(family => (Family: family, Face: SKFontManager.Default.MatchFamily(family, new SKFontStyle(w, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))))
            .Where(m => m.Face is not null && m.Face.FamilyName.Equals(m.Family, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Face)
            .FirstOrDefault());

    private static SKTypeface LoadTypeface(string resource)
    {
        using Stream stream = typeof(MiniFontCompleter).Assembly.GetManifestResourceStream($"MiniThemeStudio.Fonts.Resources.{resource}")
            ?? throw new InvalidOperationException($"The embedded font {resource} is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return SKTypeface.FromData(SKData.CreateCopy(copy.ToArray()))
            ?? throw new InvalidDataException($"The embedded font {resource} can't be read.");
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
    }

    // rows of glyphs across the width; returns where they went and the height they take
    private static (List<(FontGlyph Glyph, SKBitmap? Bitmap, SKRectI Source)> Placed, int Height) Shelve(
        List<(FontGlyph Glyph, SKBitmap? Bitmap, SKRectI Source)> pieces, int width)
    {
        var placed = new List<(FontGlyph Glyph, SKBitmap? Bitmap, SKRectI Source)>();
        int x = Padding, y = Padding, rowHeight = 0;
        foreach ((FontGlyph Glyph, SKBitmap? Bitmap, SKRectI Source) piece in pieces)
        {
            if (x + piece.Glyph.Width + Padding > width)
            {
                x = Padding;
                y += rowHeight + Padding;
                rowHeight = 0;
            }

            placed.Add((piece.Glyph with { X = x, Y = y, Page = 0 }, piece.Bitmap, piece.Source));
            x += piece.Glyph.Width + Padding;
            rowHeight = Math.Max(rowHeight, piece.Glyph.Height);
        }

        return (placed, y + rowHeight + Padding);
    }

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
