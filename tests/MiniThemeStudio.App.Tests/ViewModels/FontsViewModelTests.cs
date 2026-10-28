using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Infrastructure.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class FontsViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public FontsViewModelTests()
    {
        var basePath = _app.CreateBase();
        MiniFontWriter.Write(SmallFont(), Path.Combine(Directory.CreateDirectory(Path.Combine(basePath, "resources", "fonts")).FullName, "s.fnt"));
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    // a theme keeps its fonts beside resources, named as Font Remount reads them
    private string DraftFont => Path.Combine(_app.Window.CurrentDraftPath!, "fonts", "small.fnt");

    [Fact]
    public void LoadsTheFontOfTheBaseWithItsPreviews()
    {
        var fonts = _app.Window.FontsTab;

        Assert.Equal("Small UI Font (s)", fonts.SelectedFont?.DisplayName);
        Assert.Equal(4, fonts.FontGlyphs.Count);
        Assert.NotNull(fonts.FontTexturePreview);
        Assert.NotNull(fonts.FontSamplePreview);
        Assert.NotNull(fonts.FontValidation);
        Assert.True(fonts.CanReplaceFont);

        // the scripts the font already has are ticked
        Assert.True(fonts.ConvertCyrillic);
        Assert.False(fonts.ConvertJapanese);
    }

    [Fact]
    public void GlyphsCanBeFoundByCharacterOrCode()
    {
        var fonts = _app.Window.FontsTab;

        fonts.GlyphSearchText = "Ж";
        Assert.Equal('Ж', Assert.Single(fonts.FilteredFontGlyphs).Character);

        fonts.GlyphSearchText = "U+0041";
        Assert.Equal('A', Assert.Single(fonts.FilteredFontGlyphs).Character);

        fonts.GlyphSearchText = "";
        Assert.Equal(4, fonts.FilteredFontGlyphs.Count);
    }

    [Fact]
    public async Task FillingAddsTheMissingCharactersToADraftCopy()
    {
        var fonts = _app.Window.FontsTab;

        await fonts.CompleteFontCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(DraftFont));
        Assert.True(MiniFontReader.Read(DraftFont).HasGlyph('R'));
        Assert.True(fonts.FontGlyphs.Count > 4);
        Assert.Equal("Filled Small UI Font (s) from the base font.", _app.Window.Status);
    }

    [Fact]
    public async Task ExportingWritesTheFontAndItsTexture()
    {
        var fonts = _app.Window.FontsTab;
        var fontTarget = Path.Combine(_app.Root, "s_export.fnt");
        var textureTarget = Path.Combine(_app.Root, "s_texture.png");
        _app.Files.Paths.Enqueue(fontTarget);
        _app.Files.Paths.Enqueue(textureTarget);

        await fonts.ExportFontFileCommand.ExecuteAsync(null);
        await fonts.ExportTextureCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal(4, MiniFontReader.Read(fontTarget).Glyphs.Count);
        Assert.True(new FileInfo(textureTarget).Length > 0);
    }

    [Fact]
    public async Task TheNesFontsInTheConsolesFolderAreListedAndAPlaceholderCanBeFilled()
    {
        // the NES keeps l, m and s in fonts/nes; its title font is a 1 x 1 placeholder with a space only
        var basePath = Path.Combine(_app.Root, "base");
        MiniFontWriter.Write(SmallFont(), Path.Combine(Directory.CreateDirectory(Path.Combine(basePath, "resources", "fonts", "nes")).FullName, "m.fnt"));
        using (var pixel = new SKBitmap(1, 1))
        using (var png = SKImage.FromBitmap(pixel).Encode(SKEncodedImageFormat.Png, 100))
        {
            MiniFontWriter.Write(new MiniFont(
                formatType: 1,
                info: new FontInfo("Title", 47, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
                common: new FontCommon(47, 40, 1, 1, 1, false, 0, 0, 0, 0),
                pageNames: ["title.png"],
                glyphs: [new FontGlyph(' ', 0, 0, 2, 2, 0, 0, 12, 0, 15)],
                kerningPairs: [],
                texturePngBytes: png.ToArray()), Path.Combine(basePath, "resources", "fonts", "title.fnt"));
        }

        _app.Session.Reload();
        var fonts = _app.Window.FontsTab;

        Assert.Equal(["title", "m", "s"], fonts.Fonts.Select(font => font.Name));
        Assert.Equal("fonts/medium.fnt", fonts.Fonts.Single(font => font.Name == "m").RelativePath);

        fonts.SelectedFont = fonts.Fonts[0];
        await fonts.CompleteFontCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(fonts.FontGlyphs.Count > 1);
    }

    [Fact]
    public void AThemesOwnFontsFolderReplacesTheBaseFonts()
    {
        // as in the om_themes: fonts/small.fnt beside resources
        var small = SmallFont();
        MiniFontWriter.Write(new MiniFont(small.FormatType, small.Info, small.Common, small.PageNames, small.Glyphs.Take(2).ToList(), small.KerningPairs, small.TexturePngBytes), DraftFont);
        _app.Session.Reload();
        var fonts = _app.Window.FontsTab;

        var font = fonts.Fonts.Single(candidate => candidate.Name == "s");
        Assert.False(font.IsFromBaseUi);
        Assert.Equal(2, font.GlyphCount);
        Assert.Equal(DraftFont, font.FilePath);
    }

    [Fact]
    public async Task AFontAnOlderDraftKeptInResourcesMovesToTheFontsFolder()
    {
        var older = Path.Combine(_app.Window.CurrentDraftPath!, "resources", "fonts", "s.fnt");
        Directory.CreateDirectory(Path.GetDirectoryName(older)!);
        MiniFontWriter.Write(SmallFont(), older);
        _app.Session.Reload();
        var fonts = _app.Window.FontsTab;

        await fonts.CompleteFontCommand.ExecuteAsync(null);

        Assert.True(File.Exists(DraftFont));
        Assert.False(File.Exists(older));
    }

    // capitals 20px tall standing on y = 24, plus one Cyrillic letter
    private static MiniFont SmallFont()
    {
        using var bitmap = new SKBitmap(64, 64);
        using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return new MiniFont(
            formatType: 1,
            info: new FontInfo("Small", 28, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
            common: new FontCommon(28, 22, 64, 64, 1, false, 0, 0, 0, 0),
            pageNames: ["s.png"],
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
}
