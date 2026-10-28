using Avalonia.Media;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.Infrastructure.Tests.Themes;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ColorsViewModelTests : IDisposable
{
    private const string Usa = "resources/scripts/env/colors_usa.lua";
    private const string Jpn = "resources/scripts/env/colors_jpn.lua";

    private readonly TestApp _app = new();

    public ColorsViewModelTests()
    {
        var basePath = _app.CreateBase();
        TestApp.WriteBytes(basePath, Usa, PaletteFixtures.SampleBytes);
        TestApp.WriteBytes(basePath, Jpn, PaletteFixtures.SampleBytes);

        // a label of the Base draws its text with font_home_hud
        TestApp.WriteFile(basePath, "resources/prefab/title.scn", """
            {"rootWorldNode":{"name":"Title","components":[],"children":[
              {"name":"hud","components":[{"type":"LabelComponent","text":"Home","properties":{"colorName":"font_home_hud"}}],"children":[]}
            ]}}
            """);
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    private string DraftPath => _app.Window.CurrentDraftPath!;

    [Fact]
    public void ShowsOnlyTheColoursTheScenesUseUnlessAsked()
    {
        var colors = _app.Window.ColorsTab;

        Assert.Equal(["jpn", "usa"], colors.Palettes.Select(palette => palette.Region));
        var hud = Assert.Single(colors.PaletteColors);
        Assert.Equal("font_home_hud", hud.Name);
        Assert.Equal("#FFFFFF", hud.Hex);
        Assert.Equal("Used by 1 scene components:", colors.ColorUsageSummary);

        colors.ShowUnusedColors = true;

        Assert.Equal(3, colors.PaletteColors.Count);
        Assert.Equal("font_home_hud", colors.SelectedPaletteColor?.Name);
    }

    [Fact]
    public async Task ApplyingWritesTheColourToEveryRegion()
    {
        var colors = _app.Window.ColorsTab;
        colors.EditColor = Color.FromArgb(255, 190, 0, 26);

        await colors.ApplyColorCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal(PaletteFixtures.SampleEditedBytes, File.ReadAllBytes(Path.Combine(DraftPath, Usa)));
        Assert.Equal(PaletteFixtures.SampleEditedBytes, File.ReadAllBytes(Path.Combine(DraftPath, Jpn)));
        Assert.Equal("#BE001A", colors.SelectedPaletteColor?.Hex);
        Assert.Equal("font_home_hud: #BE001A in colors_jpn, colors_usa.", _app.Window.Status);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task ApplyingToOneRegionLeavesTheOthersOnTheBase()
    {
        var colors = _app.Window.ColorsTab;
        colors.ApplyColorToAllRegions = false;
        colors.EditColor = Color.FromArgb(255, 190, 0, 26);

        await colors.ApplyColorCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(DraftPath, Jpn)));
        Assert.False(File.Exists(Path.Combine(DraftPath, Usa)));
        Assert.True(colors.Palettes.Single(palette => palette.Region == "usa").IsFromBaseUi);
    }
}
