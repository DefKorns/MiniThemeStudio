using System.Collections.Generic;

namespace MiniThemeStudio.Core.Themes;

public interface IThemeAssetManager
{
    string EnsureDraftCopy(string themeDraftPath, string relativePath, string sourcePath);

    IReadOnlyList<WallpaperAsset> GetWallpapers(string themeDraftPath, string? baseUiPath = null);
    void ReplaceWallpaper(string targetImagePath, string replacementImagePath, string? templateJsonPath = null);
    void ExportWallpaper(WallpaperAsset wallpaper, string destinationPath);

    IReadOnlyList<ExtraAsset> GetExtraAssets(string themeDraftPath, string? baseUiPath = null);
    void AddExtraAsset(string themeDraftPath, string relativeDestinationPath, string sourceImagePath, string? sourceJsonPath = null);
    void ReplaceExtraAsset(string targetImagePath, string replacementImagePath, string? templateJsonPath = null);
    void DeleteExtraAsset(string imagePath, string? jsonPath = null);
    void ExportExtraAsset(ExtraAsset asset, string destinationPath);

    void ExportPackedSprite(string atlasImagePath, SpriteRegion sprite, string destinationPath);
}
