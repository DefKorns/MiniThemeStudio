using System.Collections.Generic;

namespace MiniThemeStudio.Core.Themes;

public interface IThemeAssetManager
{
    string EnsureDraftCopy(string themeDraftPath, string relativePath, string sourcePath);

    IReadOnlyList<WallpaperAsset> GetWallpapers(string themeDraftPath, string? baseUiPath = null);
    void ReplaceWallpaper(string targetImagePath, string replacementImagePath, string? templateJsonPath = null);
    void ExportWallpaper(WallpaperAsset wallpaper, string destinationPath);

    IReadOnlyList<ExtraAsset> GetExtraAssets(string themeDraftPath, string? baseUiPath = null);
    void ReplaceExtraAsset(string targetImagePath, string replacementImagePath, string? templateJsonPath = null);
    void DeleteExtraAsset(string imagePath, string? jsonPath = null);
    void ExportExtraAsset(ExtraAsset asset, string destinationPath);

    void ProvideMissingTexture(string themeDraftPath, MissingTexture texture, string sourceImagePath);

    void ExportPackedSprite(string atlasImagePath, SpriteRegion sprite, string destinationPath);

    IReadOnlyList<MiniThemeStudio.Core.Fonts.FontAsset> GetFonts(string themeDraftPath, string? baseUiPath = null);
    MiniThemeStudio.Core.Fonts.MiniFont LoadFont(string fontFilePath);
    void ReplaceFontTexture(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, string replacementPngPath);
    void ReplaceFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, string sourceFontPath);
    void ExportFontTexture(MiniThemeStudio.Core.Fonts.FontAsset font, string destinationPngPath);
    void ExportFont(MiniThemeStudio.Core.Fonts.FontAsset font, string destinationFontPath);
    void ImportTrueTypeFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset targetFont, string ttfOrZipPath, int? fontSize = null, bool antiAlias = false,
        MiniThemeStudio.Core.Fonts.ExtraScripts extraScripts = MiniThemeStudio.Core.Fonts.ExtraScripts.None, string? fillFontPath = null);
    void CompleteFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, MiniThemeStudio.Core.Fonts.ExtraScripts extraScripts, string? fillFontPath = null);

    IReadOnlyList<MiniThemeStudio.Core.Audio.BgmTrack> GetBgmTracks(string themeDraftPath, string? baseUiPath = null);
    void ReplaceBgmTrack(string themeDraftPath, string trackName, string sourceAudioFilePath, string? baseUiPath = null);
    void MuteBgmTrack(string themeDraftPath, string trackName, string? baseUiPath = null);
    void RevertBgmTrack(string themeDraftPath, string trackName);
}
