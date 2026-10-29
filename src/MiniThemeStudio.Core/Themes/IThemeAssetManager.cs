using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Fonts;

namespace MiniThemeStudio.Core.Themes;

public interface IThemeAssetManager
{
    string EnsureDraftCopy(string themeDraftPath, string relativePath, string sourcePath);

    IReadOnlyList<WallpaperAsset> GetWallpapers(string themeDraftPath, string? baseUiPath = null);
    void ReplaceWallpaper(string targetImagePath, string replacementImagePath, string? templateJsonPath = null);
    void ExportWallpaper(WallpaperAsset wallpaper, string destinationPath);

    IReadOnlyList<ExtraAsset> GetExtraAssets(string themeDraftPath, string? baseUiPath = null);
    void ReplaceExtraAsset(string targetImagePath, string replacementImagePath, string? templateJsonPath = null);
    // removes an image of the theme (relativePath inside resources/, e.g. layout/asset/sprite/wallpaper/hills.png):
    // an image the theme added also leaves dependencies.json, a replacement of a Base image makes the Base one
    // show again; an added image a scene still draws is kept
    void DeleteThemeImage(string themeDraftPath, string? baseUiPath, string relativePath);
    void ExportExtraAsset(ExtraAsset asset, string destinationPath);

    void ProvideMissingTexture(string themeDraftPath, MissingTexture texture, string sourceImagePath);

    void ExportPackedSprite(string atlasImagePath, SpriteRegion sprite, string destinationPath);

    IReadOnlyList<FontAsset> GetFonts(string themeDraftPath, string? baseUiPath = null);
    MiniFont LoadFont(string fontFilePath);
    void ReplaceFontTexture(string themeDraftPath, FontAsset font, string replacementPngPath);
    void ReplaceFont(string themeDraftPath, FontAsset font, string sourceFontPath);
    void ExportFontTexture(FontAsset font, string destinationPngPath);
    void ExportFont(FontAsset font, string destinationFontPath);
    void ImportTrueTypeFont(string themeDraftPath, FontAsset targetFont, string ttfOrZipPath, int? fontSize = null, bool antiAlias = false,
        ExtraScripts extraScripts = ExtraScripts.None, string? fillFontPath = null);
    void CompleteFont(string themeDraftPath, FontAsset font, ExtraScripts extraScripts, string? fillFontPath = null);

    IReadOnlyList<BgmTrack> GetBgmTracks(string themeDraftPath, string? baseUiPath = null);
    void ReplaceBgmTrack(string themeDraftPath, string trackName, string sourceAudioFilePath, string? baseUiPath = null);
    void MuteBgmTrack(string themeDraftPath, string trackName, string? baseUiPath = null);
    void RevertBgmTrack(string themeDraftPath, string trackName);
}
