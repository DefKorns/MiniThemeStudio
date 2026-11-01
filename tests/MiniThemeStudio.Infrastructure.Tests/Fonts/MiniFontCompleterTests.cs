using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Infrastructure.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Fonts;

public class MiniFontCompleterTests
{
    private const ExtraScripts AllScripts = ExtraScripts.Japanese | ExtraScripts.Cyrillic | ExtraScripts.Greek;

    // capitals 20px tall standing on y = 24, plus one Cyrillic letter
    private static MiniFont SmallFont()
    {
        using var bitmap = new SKBitmap(64, 64);
        using SKData png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return new MiniFont(
            formatType: 1,
            info: new FontInfo("Small", 28, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
            common: new FontCommon(28, 22, 64, 64, 1, false, 0, 0, 0, 0),
            pageNames: ["small.png"],
            glyphs:
            [
                new FontGlyph('A', 2, 2, 10, 20, 0, 4, 12, 0, 15),
                new FontGlyph('B', 14, 2, 10, 20, 0, 4, 12, 0, 15),
                new FontGlyph('H', 26, 2, 10, 20, 0, 4, 12, 0, 15),
                new FontGlyph('Ж', 38, 2, 10, 20, 0, 4, 12, 0, 15),
            ],
            kerningPairs: [],
            texturePngBytes: png.ToArray());
    }

    [Fact]
    public void MissingCapitalsMatchTheFontsOwnCapitals()
    {
        MiniFont completed = MiniFontCompleter.Complete(SmallFont(), ExtraScripts.None);

        FontGlyph r = completed.GetGlyph('R')!;
        Assert.InRange(r.Height, 19, 21);
        Assert.InRange(r.YOffset + r.Height, 23, 25);
        Assert.Equal(28, completed.Common.LineHeight);
    }

    [Fact]
    public void APlaceholderFontIsFilledIntoASquareishTexture()
    {
        // the NES and Famicom title.fnt: a space on a 1 x 1 texture
        using var pixel = new SKBitmap(1, 1);
        using SKData png = SKImage.FromBitmap(pixel).Encode(SKEncodedImageFormat.Png, 100);
        var placeholder = new MiniFont(
            formatType: 1,
            info: new FontInfo("Title", 47, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
            common: new FontCommon(47, 40, 1, 1, 1, false, 0, 0, 0, 0),
            pageNames: ["title.png"],
            glyphs: [new FontGlyph(' ', 0, 0, 2, 2, 0, 0, 12, 0, 15)],
            kerningPairs: [],
            texturePngBytes: png.ToArray());

        MiniFont completed = MiniFontCompleter.Complete(placeholder, ExtraScripts.None);

        Assert.True(completed.HasGlyph('A'));
        Assert.True(completed.Common.ScaleH <= completed.Common.ScaleW);
        Assert.Equal(0, completed.Common.ScaleW & (completed.Common.ScaleW - 1));
        Assert.Equal(0, completed.Common.ScaleH & (completed.Common.ScaleH - 1));
    }

    [Fact]
    public void RedrawingDrawsTheFontsOwnLettersAgainInOneTypeface()
    {
        // the small font's A, B and H have no pixels: redrawn, they have
        MiniFont font = SmallFont();

        MiniFont completed = MiniFontCompleter.Complete(font, ExtraScripts.None, redrawText: true);

        using SKBitmap atlas = SKBitmap.Decode(completed.TexturePngBytes);
        FontGlyph a = completed.GetGlyph('A')!;
        long ink = 0;
        for (int y = a.Y; y < a.Y + a.Height; y++)
        {
            for (int x = a.X; x < a.X + a.Width; x++)
            {
                ink += atlas.GetPixel(x, y).Alpha;
            }
        }

        Assert.True(ink > 0);
        Assert.InRange(a.Height, 19, 21);
    }

    [Fact]
    public void RedrawingKeepsTheButtonIconsOfTheFont()
    {
        // console fonts keep their button icons in the private use area
        MiniFont font = SmallFont();
        FontGlyph icon = new(0xE000, 50, 2, 10, 20, 0, 4, 12, 0, 15);
        MiniFont withIcon = new(font.FormatType, font.Info, font.Common, font.PageNames, [.. font.Glyphs, icon], font.KerningPairs, font.TexturePngBytes);

        MiniFont completed = MiniFontCompleter.Complete(withIcon, ExtraScripts.Cyrillic, redrawText: true);

        Assert.Equal((10, 20, 12), (completed.GetGlyph(0xE000)!.Width, completed.GetGlyph(0xE000)!.Height, completed.GetGlyph(0xE000)!.XAdvance));
        Assert.True(completed.HasGlyph('Ж'));
    }

    [Fact]
    public void FillingOnlyKeepsTheFontsOwnLetters()
    {
        MiniFont completed = MiniFontCompleter.Complete(SmallFont(), ExtraScripts.None);

        Assert.Equal(SmallFont().GetGlyph('A')!.Width, completed.GetGlyph('A')!.Width);
    }

    [Fact]
    public void EveryCharsetCharacterIsThereWhenAllScriptsAreAsked()
    {
        MiniFont completed = MiniFontCompleter.Complete(SmallFont(), AllScripts);

        var missing = FontCharsets.Build(AllScripts).Where(c => c != ' ' && !completed.HasGlyph(c)).ToList();
        Assert.True(missing.Count <= 1, new string([.. missing])); // ⅿ is in neither Noto
        Assert.True(completed.HasGlyph('削'));
        Assert.True(completed.HasGlyph('ά'));
    }

    [Fact]
    public void FilledCharactersFollowTheWeightOfTheFontsOwnStrokes()
    {
        double FilledCoverage(int weight)
        {
            using Stream stream = typeof(MiniFontCompleter).Assembly.GetManifestResourceStream($"MiniThemeStudio.Fonts.Resources.NotoSans-W{weight}.ttf")!;
            MiniFont font = new TrueTypeFontRasterizer().Rasterize(stream, new TrueTypeRasterOptions(FontSize: 40, AntiAlias: true, CharacterSet: [.. "ABCDEFGHIJKLMNOPQ0123456789"]));
            FontGlyph r = font.GetGlyph('R')!;
            using var atlas = SKBitmap.Decode(font.TexturePngBytes);
            long alpha = 0;
            for (int y = r.Y; y < r.Y + r.Height; y++)
            {
                for (int x = r.X; x < r.X + r.Width; x++)
                {
                    alpha += atlas.GetPixel(x, y).Alpha;
                }
            }

            return alpha / (255.0 * r.Width * r.Height);
        }

        Assert.True(FilledCoverage(700) > FilledCoverage(200) * 1.5);
    }

    [Fact]
    public void TheUsersFillFontComesFirst()
    {
        double FilledCoverage(int weight)
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                using (Stream stream = typeof(MiniFontCompleter).Assembly.GetManifestResourceStream($"MiniThemeStudio.Fonts.Resources.NotoSans-W{weight}.ttf")!)
                using (FileStream file = System.IO.File.Create(path))
                {
                    stream.CopyTo(file);
                }

                MiniFont completed = MiniFontCompleter.Complete(SmallFont(), ExtraScripts.None, path);
                FontGlyph r = completed.GetGlyph('R')!;
                using var atlas = SKBitmap.Decode(completed.TexturePngBytes);
                long alpha = 0;
                for (int y = r.Y; y < r.Y + r.Height; y++)
                {
                    for (int x = r.X; x < r.X + r.Width; x++)
                    {
                        alpha += atlas.GetPixel(x, y).Alpha;
                    }
                }

                return alpha / (255.0 * r.Width * r.Height);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        Assert.True(FilledCoverage(700) > FilledCoverage(200) * 1.5);
    }

    [Fact]
    public void GlyphsTheFontHasAreKeptAsTheyAre()
    {
        MiniFont completed = MiniFontCompleter.Complete(SmallFont(), ExtraScripts.Cyrillic);

        Assert.Equal(12, completed.GetGlyph('Ж')!.XAdvance);
    }

    [Fact]
    public void ScriptsNotAskedForAreLeftOutAndDropped()
    {
        MiniFont completed = MiniFontCompleter.Complete(SmallFont(), ExtraScripts.None);

        Assert.True(completed.HasGlyph('A'));
        Assert.False(completed.HasGlyph('Ж'));
        Assert.False(completed.HasGlyph('あ'));
    }

    [Fact]
    public void GlyphsStayInsideTheAtlasWithoutOverlapping()
    {
        MiniFont completed = MiniFontCompleter.Complete(SmallFont(), ExtraScripts.Japanese);

        var boxes = completed.Glyphs.Where(g => g.Width > 0).Select(g => new SKRectI(g.X, g.Y, g.X + g.Width, g.Y + g.Height)).ToList();
        Assert.All(boxes, b => Assert.True(b.Right <= completed.Common.ScaleW && b.Bottom <= completed.Common.ScaleH));
        for (int i = 1; i < boxes.Count; i++)
        {
            Assert.True(SKRectI.Intersect(boxes[i - 1], boxes[i]).IsEmpty);
        }
    }
}
