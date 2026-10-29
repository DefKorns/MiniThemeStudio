using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class NesFamilyPalettesTests : IDisposable
{
    private const string Nes = "resources/scripts/colors_nes.lua";
    private const string Famicom = "resources/scripts/colors_hvc.lua";

    private readonly string _baseUi = Directory.CreateTempSubdirectory().FullName;
    private readonly string _draft = Directory.CreateTempSubdirectory().FullName;
    private readonly ThemePaletteEditor _editor = new();

    public void Dispose()
    {
        Directory.Delete(_baseUi, true);
        Directory.Delete(_draft, true);
    }

    [Fact]
    public void NesBaseOffersTheFamicomPaletteToo()
    {
        Place(_baseUi, Nes);

        IReadOnlyList<ThemePalette> palettes = _editor.GetPalettes(_draft, _baseUi);

        Assert.Equal(["hvc", "nes"], palettes.Select(p => p.Region).Order());
        ThemePalette famicom = palettes.Single(p => p.Region == "hvc");
        Assert.Equal(PaletteColor.FromBytes("font_home_hud", 167, 0, 0, 255), famicom.Colors.Single(c => c.Name == "font_home_hud"));
        Assert.Equal(PaletteColor.FromBytes("bg_base", 235, 226, 213, 255), famicom.Colors.Single(c => c.Name == "bg_base"));
        Assert.False(File.Exists(Path.Combine(_draft, Famicom)));
    }

    [Fact]
    public void FamicomBaseOffersTheNesPaletteToo()
    {
        Place(_baseUi, Famicom);

        IReadOnlyList<ThemePalette> palettes = _editor.GetPalettes(_draft, _baseUi);

        ThemePalette nes = palettes.Single(p => p.Region == "nes");
        Assert.Equal(PaletteColor.FromBytes("font_home_hud", 219, 43, 0, 255), nes.Colors.Single(c => c.Name == "font_home_hud"));
    }

    [Fact]
    public void ChangingAColourWritesBothPalettesToTheDraft()
    {
        Place(_baseUi, Nes);
        var color = PaletteColor.FromBytes("font_resume_title", 10, 20, 30, 255);

        _editor.SetColor(_draft, _baseUi, color, [Nes, Famicom]);

        foreach (string path in new[] { Nes, Famicom })
        {
            Assert.Equal(color, LuaColorPalette.Read(Path.Combine(_draft, path)).Colors.Single(c => c.Name == "font_resume_title"));
        }

        Assert.Equal(PaletteColor.FromBytes("font_home_hud", 167, 0, 0, 255), LuaColorPalette.Read(Path.Combine(_draft, Famicom)).Colors.Single(c => c.Name == "font_home_hud"));
    }

    [Fact]
    public void ShonenBaseHasNoNesPalette()
    {
        Place(_baseUi, Famicom);
        File.WriteAllText(Path.Combine(_baseUi, "init.json"), """{"engineFormat": 10}""");

        Assert.Equal(["hvc"], _editor.GetPalettes(_draft, _baseUi).Select(p => p.Region));
    }

    private static void Place(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PaletteFixtures.SampleBytes);
    }
}
