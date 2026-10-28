using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemePaletteEditorTests : IDisposable
{
    private const string Usa = "resources/scripts/env/colors_usa.lua";
    private const string Jpn = "resources/scripts/env/colors_jpn.lua";

    private readonly string _baseUi = Directory.CreateTempSubdirectory().FullName;
    private readonly string _draft = Directory.CreateTempSubdirectory().FullName;
    private readonly ThemePaletteEditor _editor = new();

    public ThemePaletteEditorTests()
    {
        Place(_baseUi, Usa, PaletteFixtures.SampleBytes);
        Place(_baseUi, Jpn, PaletteFixtures.SampleBytes);
    }

    public void Dispose()
    {
        Directory.Delete(_baseUi, true);
        Directory.Delete(_draft, true);
    }

    [Fact]
    public void ThemeWithoutPalettesShowsTheBaseOnes()
    {
        IReadOnlyList<ThemePalette> palettes = _editor.GetPalettes(_draft, _baseUi);

        Assert.Equal(["jpn", "usa"], palettes.Select(p => p.Region));
        Assert.All(palettes, p => Assert.True(p.IsFromBaseUi));
        Assert.Equal(3, palettes[0].Colors.Count);
    }

    [Fact]
    public void ThemePaletteWinsOverTheBase()
    {
        Place(_draft, Usa, PaletteFixtures.SampleEditedBytes);

        IReadOnlyList<ThemePalette> palettes = _editor.GetPalettes(_draft, _baseUi);

        ThemePalette usa = palettes.Single(p => p.Region == "usa");
        Assert.False(usa.IsFromBaseUi);
        Assert.Equal(PaletteColor.FromBytes("font_home_hud", 190, 0, 26, 255), usa.Colors.Single(c => c.Name == "font_home_hud"));
        Assert.True(palettes.Single(p => p.Region == "jpn").IsFromBaseUi);
    }

    [Fact]
    public void SettingAColourForEveryRegionCopiesTheBasePalettesIntoTheDraft()
    {
        var color = PaletteColor.FromBytes("font_home_hud", 190, 0, 26, 255);

        _editor.SetColor(_draft, _baseUi, color, [Usa, Jpn]);

        Assert.Equal(PaletteFixtures.SampleEditedBytes, File.ReadAllBytes(Path.Combine(_draft, Usa)));
        Assert.Equal(PaletteFixtures.SampleEditedBytes, File.ReadAllBytes(Path.Combine(_draft, Jpn)));
        Assert.Equal(PaletteFixtures.SampleBytes, File.ReadAllBytes(Path.Combine(_baseUi, Usa)));
        Assert.All(_editor.GetPalettes(_draft, _baseUi), p => Assert.False(p.IsFromBaseUi));
    }

    [Fact]
    public void SettingAColourForOneRegionLeavesTheOthersOnTheBase()
    {
        _editor.SetColor(_draft, _baseUi, PaletteColor.FromBytes("bg_base", 0, 0, 0, 255), [Jpn]);

        Assert.False(File.Exists(Path.Combine(_draft, Usa)));
        Assert.True(_editor.GetPalettes(_draft, _baseUi).Single(p => p.Region == "usa").IsFromBaseUi);
    }

    [Fact]
    public void UsagesComeFromTheScenesWithTheThemesWinning()
    {
        const string scene = "resources/defaultscene.scn";
        Place(_baseUi, scene, Scene("font_home_hud", "@sys_hud_Menu"));
        Place(_baseUi, "resources/prefab/dialog.scn", Scene("font_dialog_text", null));
        Place(_draft, scene, Scene("font_resume_title", "@sys_resume_Title"));

        IReadOnlyList<PaletteColorUsage> usages = _editor.GetUsages(_draft, _baseUi);

        Assert.Equal(
            [
                new PaletteColorUsage("font_resume_title", scene, "/root/label", "@sys_resume_Title"),
                new PaletteColorUsage("font_dialog_text", "resources/prefab/dialog.scn", "/root/label", null),
            ],
            usages);
    }

    [Fact]
    public void AColourNamedInAScriptCountsAsUsed()
    {
        Place(_baseUi, "resources/scripts/home.lua", System.Text.Encoding.UTF8.GetBytes("LJ...bg_base..."));

        IReadOnlyList<PaletteColorUsage> usages = _editor.GetUsages(_draft, _baseUi);

        Assert.Equal([new PaletteColorUsage("bg_base", "resources/scripts/home.lua", "(Lua script)", null)], usages);
    }

    private static byte[] Scene(string colorName, string? text) => System.Text.Encoding.UTF8.GetBytes(
        "{\"rootWorldNode\":{\"name\":\"root\",\"components\":[],\"children\":[{\"name\":\"label\",\"components\":[" +
        "{\"type\":\"LabelComponent\",\"text\":\"" + text + "\",\"properties\":{\"colorName\":\"" + colorName + "\"}}]}]}}");

    private static void Place(string root, string relativePath, byte[] bytes)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
