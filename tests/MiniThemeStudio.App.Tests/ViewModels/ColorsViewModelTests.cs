using Avalonia.Media;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Infrastructure.Tests.Themes;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ColorsViewModelTests : IDisposable
{
    private const string Usa = "resources/scripts/env/colors_usa.lua";
    private const string Jpn = "resources/scripts/env/colors_jpn.lua";

    private readonly TestApp _app = new();

    public ColorsViewModelTests()
    {
        string basePath = _app.CreateBase();
        TestApp.WriteBytes(basePath, Usa, PaletteFixtures.SampleBytes);
        TestApp.WriteBytes(basePath, Jpn, PaletteFixtures.SampleBytes);

        // a label of the Base draws its text with font_home_hud
        TestApp.WriteFile(basePath, "resources/prefab/title.scn", """
            {"rootWorldNode":{"name":"Title","components":[],"children":[
              {"name":"hud","components":[{"type":"LabelComponent","text":"Home","properties":{"colorName":"font_home_hud"}}],"children":[]},
              {"name":"menu","components":[{"type":"LabelComponent","text":"@sys_menu","properties":{"colorName":"font_home_hud"}}],"children":[]}
            ]}}
            """);
        // the wall behind the home screen of the SNES: a 1x1 picture is enough
        TestApp.WriteBytes(basePath, "resources/layout/asset/sprite/wallpaper/wall.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        TestApp.WriteFile(basePath, "resources/strings/usa_en/strings.lng", """{"strings":{"sys_menu":"Menu"}}""");
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    private string DraftPath => _app.Window.CurrentDraftPath!;

    [Fact]
    public void ShowsOnlyTheColoursTheScenesUseUnlessAsked()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;

        Assert.Equal(["jpn", "usa"], colors.Palettes.Select(palette => palette.Region));
        Assert.Equal("A change applies to Japan, USA.", colors.RegionsText);
        PaletteColorRow hud = Assert.Single(colors.PaletteColors);
        Assert.Equal("font_home_hud", hud.Name);
        Assert.Equal("#FFFFFF", hud.Hex);

        colors.ShowUnusedColors = true;

        Assert.Equal(3, colors.PaletteColors.Count);
        Assert.Equal("font_home_hud", colors.SelectedPaletteColor?.Name);
    }

    [Fact]
    public async Task ApplyingWritesTheColourToEveryRegion()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;
        colors.EditColor = Color.FromArgb(255, 190, 0, 26);

        await colors.ApplyColorCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal(PaletteFixtures.SampleEditedBytes, File.ReadAllBytes(Path.Combine(DraftPath, Usa)));
        Assert.Equal(PaletteFixtures.SampleEditedBytes, File.ReadAllBytes(Path.Combine(DraftPath, Jpn)));
        Assert.Equal("#BE001A", colors.SelectedPaletteColor?.Hex);
        Assert.Equal("Home: info text: #BE001A in Japan, USA.", _app.Window.Status);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task ResettingAColourPutsItBackToTheBaseAndRemovesThePalettes()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;
        colors.EditColor = Color.FromArgb(255, 190, 0, 26);
        await colors.ApplyColorCommand.ExecuteAsync(null);
        Assert.True(colors.SelectedPaletteColor?.IsModified);
        Assert.True(colors.ResetColorCommand.CanExecute(null));

        await colors.ResetColorCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.False(File.Exists(Path.Combine(DraftPath, Usa)));
        Assert.False(File.Exists(Path.Combine(DraftPath, Jpn)));
        Assert.False(colors.SelectedPaletteColor?.IsModified);
        Assert.False(colors.ResetColorCommand.CanExecute(null));
        Assert.False(colors.ResetAllColorsCommand.CanExecute(null));
    }

    [Fact]
    public void ShowsTheWordsAColourDrawsAndTheScreenTheyAreOn()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;

        PaletteColorRow hud = Assert.Single(colors.PaletteColors);
        Assert.Equal("Home · Menu", hud.Samples);
        Assert.Equal("Home", hud.Group);
        Assert.True(hud.StartsGroup);
        Assert.Equal("Home", colors.SampleText);

        colors.ShowUnusedColors = true;

        Assert.All(colors.PaletteColors.Where(row => row.Name != "font_home_hud"), row => Assert.Equal("", row.Samples));
        Assert.Single(colors.PaletteColors, row => row.StartsGroup && row.Group == "Home");
    }

    [Fact]
    public void SaysWhichScreensDrawTheSelectedColour()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;

        Assert.Equal("Used on: Hud, Menu", colors.UsedOn);
    }

    [Fact]
    public void TheSampleIsDrawnOnABackgroundTheColourReadsOn()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;

        colors.EditColor = Color.FromArgb(255, 0, 0, 0);
        Color onBlack = ((ISolidColorBrush)colors.SampleBackground).Color;
        colors.EditColor = Color.FromArgb(255, 255, 255, 255);
        Color onWhite = ((ISolidColorBrush)colors.SampleBackground).Color;

        Assert.True(onBlack.R > 0xC0);
        Assert.True(onWhite.R < 0x40);
    }

    [Fact]
    public void AHomeTextSitsOnTheWallAndItsGroupOfNodes()
    {
        ColorsViewModel colors = _app.Window.ColorsTab;

        Assert.Equal("font_home_hud", colors.SelectedPaletteColor?.Name);
        Assert.True(colors.HasSampleBackdrop);
        Assert.Equal(2, colors.SampleBackdropWidth);
        Assert.True(colors.HasSampleScene);
        Assert.Equal("Menu", colors.SampleStrings["sys_menu"]);

        colors.EditColor = Color.FromArgb(255, 190, 0, 26);

        Assert.Equal(Color.FromArgb(255, 190, 0, 26), colors.SampleColors["font_home_hud"]);
    }
}
