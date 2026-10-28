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
    public static MiniFont Complete(MiniFont font, ExtraScripts scripts, string? fillFontPath = null)
    {
        ArgumentNullException.ThrowIfNull(font);

        var kept = font.Glyphs.Where(g => IsWanted(g.Id, scripts)).ToList();
        var have = kept.Select(g => g.Id).ToHashSet();
        var missing = FontCharsets.Build(scripts).Where(c => c != ' ' && !have.Contains(c)).ToList();
        if (missing.Count == 0 && kept.Count == font.Glyphs.Count)
        {
            return font;
        }

        var (textSize, baseline) = Fit(font);
        using var decoded = SKBitmap.Decode(font.TexturePngBytes);
        using var fontAtlas = decoded.ColorType == SKColorType.Rgba8888 || decoded.ColorType == SKColorType.Bgra8888
            ? decoded.Copy()
            : decoded.Copy(SKColorType.Rgba8888);
        var latinWeight = MatchWeight(font, fontAtlas, textSize, japanese: false);
        var japaneseWeight = MatchWeight(font, fontAtlas, textSize, japanese: true);
        var latin = Face(latinWeight ?? japaneseWeight ?? DefaultWeight).Latin;
        var japanese = Face(japaneseWeight ?? latinWeight ?? DefaultWeight).Japanese;
        var smooth = IsSmooth(font, fontAtlas);
        using var userPaint = UserFace(fillFontPath) is { } user ? Paint(user, textSize, smooth) : null;
        using var latinPaint = Paint(latin, textSize, smooth);
        using var japanesePaint = Paint(japanese, textSize, smooth);
        using var notoLatinPaint = Paint(NotoFace(latinWeight ?? japaneseWeight ?? DefaultWeight).Latin, textSize, smooth);
        using var notoJapanesePaint = Paint(NotoFace(japaneseWeight ?? latinWeight ?? DefaultWeight).Japanese, textSize, smooth);

        var drawn = new List<(FontGlyph Glyph, SKBitmap? Bitmap)>();
        foreach (var c in missing)
        {
            var paint = new[] { userPaint, latinPaint, japanesePaint, notoLatinPaint, notoJapanesePaint }.FirstOrDefault(p => p?.Typeface.ContainsGlyph(c) == true);
            if (paint is null)
            {
                continue;
            }

            var bounds = InkBounds(paint, c);
            var advance = paint.MeasureText(c.ToString());
            var width = (int)Math.Ceiling(bounds.Width);
            var height = (int)Math.Ceiling(bounds.Height);
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
            foreach (var (_, bitmap) in drawn)
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

        var c = (char)id;
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
        using var measure = Paint(Face(DefaultWeight).Latin, MeasureSize, false);
        var ratios = new List<double>();
        var bottoms = new List<double>();
        foreach (var glyph in font.Glyphs.Where(g => g.Height > 0 && g.Id is (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
        {
            var bounds = InkBounds(measure, (char)glyph.Id);
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

        measure.GetFontMetrics(out var metrics);
        var lineHeight = metrics.Descent - metrics.Ascent;
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

        var target = Median(samples.Select(g => Coverage(atlas, new SKRectI(g.X, g.Y, g.X + g.Width, g.Y + g.Height))).ToList());
        return Weights.MinBy(weight =>
        {
            var face = japanese ? Face(weight).Japanese : Face(weight).Latin;
            using var paint = Paint(face, textSize, true);
            var coverages = samples.Select(g => RenderedCoverage(paint, (char)g.Id)).Where(c => c > 0).ToList();
            return coverages.Count == 0 ? double.MaxValue : Math.Abs(Median(coverages) - target);
        });
    }

    // the header's smooth flag is often wrong: the font is smooth when its glyphs' edges are partly transparent
    private static bool IsSmooth(MiniFont font, SKBitmap atlas)
    {
        var pixels = atlas.GetPixelSpan();
        long inked = 0, partial = 0;
        foreach (var glyph in font.Glyphs.Where(g => g.Width > 0 && g.Height > 0).Take(40))
        {
            for (var y = glyph.Y; y < glyph.Y + glyph.Height; y++)
            {
                for (var x = glyph.X; x < glyph.X + glyph.Width; x++)
                {
                    var alpha = pixels[y * atlas.RowBytes + x * 4 + 3];
                    inked += alpha > 0 ? 1 : 0;
                    partial += alpha is > 0 and < 255 ? 1 : 0;
                }
            }
        }

        return inked == 0 ? font.Info.Smooth : partial > inked / 5;
    }

    private static double RenderedCoverage(SKPaint paint, char c)
    {
        var bounds = InkBounds(paint, c);
        var width = (int)Math.Ceiling(bounds.Width);
        var height = (int)Math.Ceiling(bounds.Height);
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
        var pixels = bitmap.GetPixelSpan();
        var rowBytes = bitmap.RowBytes;
        long alpha = 0;
        for (var y = box.Top; y < box.Bottom; y++)
        {
            var row = y * rowBytes;
            for (var x = box.Left; x < box.Right; x++)
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

        var atlasWidth = Math.Max(font.Common.ScaleW, 64);
        var placed = new List<(FontGlyph Glyph, SKBitmap? Bitmap, SKRectI Source)>();
        int x = Padding, y = Padding, rowHeight = 0;
        foreach (var piece in pieces)
        {
            if (x + piece.Glyph.Width + Padding > atlasWidth)
            {
                x = Padding;
                y += rowHeight + Padding;
                rowHeight = 0;
            }

            placed.Add((piece.Glyph with { X = x, Y = y, Page = 0 }, piece.Bitmap, piece.Source));
            x += piece.Glyph.Width + Padding;
            rowHeight = Math.Max(rowHeight, piece.Glyph.Height);
        }

        var atlasHeight = NextPowerOfTwo(y + rowHeight + Padding);
        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        // DrawBitmap turns the whole bitmap into an image on every call; the atlas is converted once
        using var atlasImage = SKImage.FromBitmap(fontAtlas);
        foreach (var (glyph, bitmap, source) in placed)
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

        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
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
        using var path = paint.GetTextPath(c.ToString(), 0, 0);
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
        using var stream = typeof(MiniFontCompleter).Assembly.GetManifestResourceStream($"MiniThemeStudio.Fonts.Resources.{resource}")
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

    private static int NextPowerOfTwo(int value)
    {
        var power = 64;
        while (power < value && power < 4096)
        {
            power *= 2;
        }

        return Math.Max(power, value);
    }
}
