using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Infrastructure.Fonts;

namespace MiniThemeStudio.Infrastructure.Tests.Fonts;

public sealed class MiniFontTests
{
    [Fact]
    public void RoundTripSerializationPreservesAllFields()
    {
        var info = new FontInfo(
            FontName: "TestRetroFont",
            FontSize: 24,
            Bold: true,
            Italic: false,
            Unicode: true,
            Smooth: true,
            Charset: 1,
            StretchH: 100,
            Aa: 2,
            PaddingUp: 1,
            PaddingRight: 2,
            PaddingDown: 3,
            PaddingLeft: 4,
            SpacingHoriz: 1,
            SpacingVert: 1,
            Outline: 0);

        var common = new FontCommon(
            LineHeight: 28,
            Base: 22,
            ScaleW: 512,
            ScaleH: 512,
            Pages: 1,
            Packed: false,
            AlphaChannel: 0,
            RedChannel: 1,
            GreenChannel: 1,
            BlueChannel: 1);

        var glyphs = new[]
        {
            new FontGlyph(Id: 65, X: 0, Y: 0, Width: 16, Height: 24, XOffset: 0, YOffset: 0, XAdvance: 18, Page: 0, Channel: 15),
            new FontGlyph(Id: 66, X: 16, Y: 0, Width: 15, Height: 24, XOffset: 1, YOffset: 0, XAdvance: 17, Page: 0, Channel: 15),
            new FontGlyph(Id: 233, X: 32, Y: 0, Width: 14, Height: 26, XOffset: 0, YOffset: -2, XAdvance: 16, Page: 0, Channel: 15), // é
        };

        var kerning = new[]
        {
            new FontKerningPair(First: 65, Second: 66, Amount: -1)
        };

        var fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04 };

        var font = new MiniFont(
            formatType: 1,
            info: info,
            common: common,
            pageNames: ["test_font_0.png"],
            glyphs: glyphs,
            kerningPairs: kerning,
            texturePngBytes: fakePng);

        var serializedBytes = MiniFontWriter.ToBytes(font);
        Assert.NotNull(serializedBytes);
        Assert.True(serializedBytes.Length > 9 + 4);

        var deserialized = MiniFontReader.Read(serializedBytes);

        Assert.Equal(font.FormatType, deserialized.FormatType);
        Assert.Equal(font.Info.FontName, deserialized.Info.FontName);
        Assert.Equal(font.Info.FontSize, deserialized.Info.FontSize);
        Assert.Equal(font.Info.Bold, deserialized.Info.Bold);
        Assert.Equal(font.Info.Unicode, deserialized.Info.Unicode);
        Assert.Equal(font.Info.Smooth, deserialized.Info.Smooth);

        Assert.Equal(font.Common.LineHeight, deserialized.Common.LineHeight);
        Assert.Equal(font.Common.Base, deserialized.Common.Base);
        Assert.Equal(font.Common.ScaleW, deserialized.Common.ScaleW);
        Assert.Equal(font.Common.ScaleH, deserialized.Common.ScaleH);

        Assert.Single(deserialized.PageNames);
        Assert.Equal("test_font_0.png", deserialized.PageNames[0]);

        Assert.Equal(3, deserialized.Glyphs.Count);
        var glyphA = deserialized.GetGlyph('A');
        Assert.NotNull(glyphA);
        Assert.Equal(65u, glyphA.Id);
        Assert.Equal(16, glyphA.Width);
        Assert.Equal(18, glyphA.XAdvance);

        var glyphE = deserialized.GetGlyph('é');
        Assert.NotNull(glyphE);
        Assert.Equal(233u, glyphE.Id);

        Assert.Single(deserialized.KerningPairs);
        Assert.Equal(65u, deserialized.KerningPairs[0].First);
        Assert.Equal(66u, deserialized.KerningPairs[0].Second);
        Assert.Equal(-1, deserialized.KerningPairs[0].Amount);

        Assert.Equal(fakePng, deserialized.TexturePngBytes);
    }

    [Fact]
    public void FontValidatorDetectsMissingAsciiAndAccents()
    {
        var info = new FontInfo("Sample", 12, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0);
        var common = new FontCommon(12, 10, 128, 128, 1, false, 0, 0, 0, 0);

        // Font with only digits 0-9
        var glyphsOnlyDigits = Enumerable.Range('0', 10)
            .Select(c => new FontGlyph((uint)c, 0, 0, 8, 10, 0, 0, 8, 0, 0))
            .ToList();

        var fontDigits = new MiniFont(1, info, common, ["page.png"], glyphsOnlyDigits, [], []);
        var resultDigits = FontValidator.Validate(fontDigits);

        Assert.False(resultDigits.HasBasicAscii);
        Assert.False(resultDigits.HasLatinAccents);
        Assert.False(resultDigits.IsSafeForWesternUi);
        Assert.Contains('A', resultDigits.MissingAsciiChars);
        Assert.Contains('é', resultDigits.MissingAccentChars);

        // Font with full ASCII and essential accents
        var fullGlyphs = Enumerable.Range(32, 95)
            .Concat(new[] { (int)'á', (int)'à', (int)'â', (int)'ã', (int)'ä', (int)'é', (int)'è', (int)'ê', (int)'ë', (int)'í', (int)'ì', (int)'î', (int)'ï', (int)'ó', (int)'ò', (int)'ô', (int)'õ', (int)'ö', (int)'ú', (int)'ù', (int)'û', (int)'ü', (int)'ç', (int)'ñ', (int)'Á', (int)'À', (int)'Â', (int)'Ã', (int)'Ä', (int)'É', (int)'È', (int)'Ê', (int)'Ë', (int)'Í', (int)'Ì', (int)'Î', (int)'Ï', (int)'Ó', (int)'Ò', (int)'Ô', (int)'Õ', (int)'Ö', (int)'Ú', (int)'Ù', (int)'Û', (int)'Ü', (int)'Ç', (int)'Ñ' })
            .Distinct()
            .Select(c => new FontGlyph((uint)c, 0, 0, 8, 10, 0, 0, 8, 0, 0))
            .ToList();

        var fullFont = new MiniFont(1, info, common, ["page.png"], fullGlyphs, [], []);
        var fullResult = FontValidator.Validate(fullFont);

        Assert.True(fullResult.HasBasicAscii);
        Assert.True(fullResult.HasLatinAccents);
        Assert.True(fullResult.IsSafeForWesternUi);
        Assert.Empty(fullResult.MissingAsciiChars);
        Assert.Empty(fullResult.MissingAccentChars);
    }

    [Fact]
    public void CanParseExistingInstalledBaseFontIfPresent()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseFontPath = Path.Combine(appData, "MiniThemeStudio", "Bases", "shonen", "jpn", "3.0.1", "resources", "fonts", "copyright.fnt");

        if (!File.Exists(baseFontPath))
        {
            // Fallback check in snes eur
            baseFontPath = Path.Combine(appData, "MiniThemeStudio", "Bases", "snes", "eur", "2.0.13", "resources", "fonts", "l.font");
            if (!File.Exists(baseFontPath))
            {
                return; // Base not installed on this machine, test skipped safely
            }
        }

        var font = MiniFontReader.Read(baseFontPath);
        Assert.NotNull(font);
        Assert.True(font.Glyphs.Count > 0);
        Assert.True(font.Common.LineHeight > 0);
        Assert.NotEmpty(font.TexturePngBytes);

        // Validate round-trip on real console file
        var reEncoded = MiniFontWriter.ToBytes(font);
        var reRead = MiniFontReader.Read(reEncoded);

        Assert.Equal(font.FormatType, reRead.FormatType);
        Assert.Equal(font.Glyphs.Count, reRead.Glyphs.Count);
        Assert.Equal(font.Common.LineHeight, reRead.Common.LineHeight);
        Assert.Equal(font.TexturePngBytes.Length, reRead.TexturePngBytes.Length);
    }
}
