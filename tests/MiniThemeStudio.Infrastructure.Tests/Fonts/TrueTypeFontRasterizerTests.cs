using System.IO;
using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Infrastructure.Fonts;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Fonts;

public sealed class TrueTypeFontRasterizerTests
{
    [Fact]
    public void CanRasterizeZippedOpenTypeOrTrueTypeFont()
    {
        var otfZip = @"E:\Downloads\super-metroid-intro-snes.otf.zip";
        var ttfZip = @"E:\Downloads\super-metroid-intro-snes.ttf.zip";

        var testPath = File.Exists(otfZip) ? otfZip : (File.Exists(ttfZip) ? ttfZip : null);
        if (testPath is null)
        {
            return; // Skip if test files not available in environment
        }

        var rasterizer = new TrueTypeFontRasterizer();
        var font = rasterizer.Rasterize(testPath, new TrueTypeRasterOptions(
            FontSize: 16,
            AntiAlias: false,
            AtlasWidth: 512));

        Assert.NotNull(font);
        Assert.Equal(1, font.FormatType);
        Assert.True(font.Glyphs.Count > 20);
        Assert.True(font.Common.LineHeight > 0);
        Assert.Equal(512, font.Common.ScaleW);
        Assert.True(font.Common.ScaleH >= 64);
        Assert.NotEmpty(font.TexturePngBytes);

        // Check PNG signature
        Assert.Equal(0x89, font.TexturePngBytes[0]);
        Assert.Equal(0x50, font.TexturePngBytes[1]);
        Assert.Equal(0x4E, font.TexturePngBytes[2]);
        Assert.Equal(0x47, font.TexturePngBytes[3]);

        // Has essential uppercase glyphs
        Assert.True(font.HasGlyph('S'));
        Assert.True(font.HasGlyph('M'));
        Assert.True(font.HasGlyph('0'));

        // Verify round-trip binary compatibility
        var bytes = MiniFontWriter.ToBytes(font);
        var roundTripped = MiniFontReader.Read(bytes);

        Assert.Equal(font.Glyphs.Count, roundTripped.Glyphs.Count);
        Assert.Equal(font.Common.LineHeight, roundTripped.Common.LineHeight);
        Assert.Equal(font.Common.ScaleW, roundTripped.Common.ScaleW);
        Assert.Equal(font.Common.ScaleH, roundTripped.Common.ScaleH);

        var val = FontValidator.Validate(font);
        Assert.NotNull(val);
    }
}
