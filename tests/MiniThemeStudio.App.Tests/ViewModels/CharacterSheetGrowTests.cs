using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Tests.Themes;
using SkiaSharp;

namespace MiniThemeStudio.App.Tests.ViewModels;

// pictures larger than the frames of a NES character, whose script can be rewritten to make the frames bigger
public sealed class CharacterSheetGrowTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly List<string> _pictures = [];

    public CharacterSheetGrowTests()
    {
        var basePath = _app.CreateBase();
        SavePng(Path.Combine(basePath, "resources", "sprites", "characters_sbm.png"), 128, 64);
        TestApp.WriteFile(basePath, "resources/sprites/characters_sbm.json", """{"texture":{"id":"sbm","width":128,"height":64},"sprites":{}}""");
        TestApp.WriteBytes(basePath, "resources/scripts/sys_wallpaper_fg0.lua", CharacterPatternFixtures.SampleBytes);
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
        for (var index = 0; index < 2; index++)
        {
            var path = Path.Combine(_app.Root, $"big{index}.png");
            SavePng(path, 27, 36);
            _pictures.Add(path);
        }

        _app.AppDialogs.SpriteSheet = new SpriteSheetResult(_pictures, new AtlasSize(27, 36));
    }

    public void Dispose() => _app.Dispose();

    private string ScriptPath => Path.Combine(_app.Window.CurrentDraftPath!, "resources", "scripts", "sys_wallpaper_fg0.lua");

    [Fact]
    public async Task MarioGrowsToFitThePicturesWhenAsked()
    {
        var characters = _app.Window.CharactersTab;
        Assert.Equal(["Mario", "Block"], characters.SheetGroups.Select(group => group.Label));
        _app.Dialogs.ChoiceAnswer = DialogResult.Accepted;

        await characters.ImportRowCommand.ExecuteAsync(characters.SheetGroups[0].Row);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal("Larger pictures", Assert.Single(_app.Dialogs.Shown).Title);
        // the theme's own copy of the script cuts Mario at the new size
        Assert.True(File.Exists(ScriptPath));
        Assert.NotEqual(CharacterPatternFixtures.SampleBytes, File.ReadAllBytes(ScriptPath));
        Assert.Equal("up to 27 × 36", characters.SelectedSheetGroup?.Row.Frames[0].SizeText);
        Assert.Equal("Mario: replaced 2 of 2 pictures from the sprite sheet. Mario is now 27 × 36.", _app.Window.Status);
    }

    [Fact]
    public async Task ThePicturesAreMadeSmallerOtherwise()
    {
        var characters = _app.Window.CharactersTab;
        _app.Dialogs.ChoiceAnswer = DialogResult.Alternate;

        await characters.ImportRowCommand.ExecuteAsync(characters.SheetGroups[0].Row);

        Assert.False(File.Exists(ScriptPath));
        Assert.StartsWith("Mario: replaced 2 of 2 pictures from the sprite sheet. Larger than the 16 × 16 of a frame", _app.Window.Status, StringComparison.Ordinal);
    }

    private static void SavePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Green);
        bitmap.SetPixel(1, 1, SKColors.White);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
    }
}
