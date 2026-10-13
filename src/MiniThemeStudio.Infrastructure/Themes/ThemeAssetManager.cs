using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeAssetManager : IThemeAssetManager
{
    private static readonly string[] CoreWallpaperNames = ["wall", "wall_near", "wall_far"];

    public IReadOnlyList<WallpaperAsset> GetWallpapers(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var draftWallpaperDir = Path.Combine(themeDraftPath, "resources", "layout", "asset", "sprite", "wallpaper");
        var baseWallpaperDir = baseUiPath != null
            ? Path.Combine(baseUiPath, "resources", "layout", "asset", "sprite", "wallpaper")
            : null;

        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(draftWallpaperDir))
        {
            foreach (var file in Directory.EnumerateFiles(draftWallpaperDir, "*.png"))
            {
                allNames.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        if (baseWallpaperDir != null && Directory.Exists(baseWallpaperDir))
        {
            foreach (var file in Directory.EnumerateFiles(baseWallpaperDir, "*.png"))
            {
                allNames.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        // Ensure core wallpapers are considered even if only .json or missing
        foreach (var core in CoreWallpaperNames)
        {
            allNames.Add(core);
        }

        var list = new List<WallpaperAsset>();
        foreach (var name in allNames.OrderBy(GetWallpaperSortKey))
        {
            var draftPng = Path.Combine(draftWallpaperDir, $"{name}.png");
            var draftJson = Path.Combine(draftWallpaperDir, $"{name}.json");

            var basePng = baseWallpaperDir != null ? Path.Combine(baseWallpaperDir, $"{name}.png") : null;
            var baseJson = baseWallpaperDir != null ? Path.Combine(baseWallpaperDir, $"{name}.json") : null;

            var pngPath = File.Exists(draftPng)
                ? draftPng
                : basePng != null && File.Exists(basePng)
                    ? basePng
                    : draftPng;

            var jsonPath = File.Exists(draftJson)
                ? draftJson
                : baseJson != null && File.Exists(baseJson)
                    ? baseJson
                    : (File.Exists(draftJson) ? draftJson : null);

            var isFromBase = !File.Exists(draftPng) && basePng != null && File.Exists(basePng);

            int width = 0;
            int height = 0;

            if (File.Exists(pngPath))
            {
                using var bmp = SKBitmap.Decode(pngPath);
                if (bmp != null)
                {
                    width = bmp.Width;
                    height = bmp.Height;
                }
            }

            var displayName = name switch
            {
                "wall" => "Main Wallpaper (wall)",
                "wall_near" => "Foreground Parallax (wall_near)",
                "wall_far" => "Distant Parallax (wall_far)",
                _ when name.StartsWith("pole", StringComparison.OrdinalIgnoreCase) => $"Column / Pillar ({name})",
                _ => $"{char.ToUpperInvariant(name[0])}{name[1..]}"
            };

            list.Add(new WallpaperAsset(name, displayName, pngPath, jsonPath, width, height, isFromBase));
        }

        return list;
    }

    private static int GetWallpaperSortKey(string name) => name switch
    {
        "wall" => 1,
        "wall_near" => 2,
        "wall_far" => 3,
        _ => 4
    };

    public void ReplaceWallpaper(string targetImagePath, string replacementImagePath, string? jsonPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementImagePath);

        if (!File.Exists(replacementImagePath))
        {
            throw new FileNotFoundException("Replacement image not found.", replacementImagePath);
        }

        using var replacementBmp = SKBitmap.Decode(replacementImagePath)
            ?? throw new InvalidDataException("Replacement file is not a valid PNG image.");

        var dir = Path.GetDirectoryName(targetImagePath)!;
        Directory.CreateDirectory(dir);

        SaveBitmapToPng(replacementBmp, targetImagePath);

        // Update or generate JSON
        var targetJsonPath = jsonPath ?? Path.ChangeExtension(targetImagePath, ".json");
        UpdateOrGenerateTextureJson(targetJsonPath, replacementBmp.Width, replacementBmp.Height);
    }

    public void ExportWallpaper(WallpaperAsset wallpaper, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(wallpaper.ImagePath))
        {
            throw new FileNotFoundException("Wallpaper image file not found.", wallpaper.ImagePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(wallpaper.ImagePath, destinationPath, true);
    }

    public IReadOnlyList<ExtraAsset> GetExtraAssets(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var draftResDir = Path.Combine(themeDraftPath, "resources");
        var baseResDir = baseUiPath != null ? Path.Combine(baseUiPath, "resources") : null;

        var map = new Dictionary<string, (string ImagePath, string? JsonPath, bool IsBase)>(StringComparer.OrdinalIgnoreCase);

        // Scan Base UI first if available
        if (baseResDir != null && Directory.Exists(baseResDir))
        {
            ScanDirectoryForExtraAssets(baseResDir, map, true);
        }

        // Overlay draft assets
        if (Directory.Exists(draftResDir))
        {
            ScanDirectoryForExtraAssets(draftResDir, map, false);
        }

        var result = new List<ExtraAsset>();
        foreach (var (relPath, info) in map)
        {
            int w = 0, h = 0;
            if (File.Exists(info.ImagePath))
            {
                using var bmp = SKBitmap.Decode(info.ImagePath);
                if (bmp != null)
                {
                    w = bmp.Width;
                    h = bmp.Height;
                }
            }

            var category = CategorizeExtraAsset(relPath);
            var displayName = Path.GetFileNameWithoutExtension(relPath);

            result.Add(new ExtraAsset(
                relPath,
                displayName,
                category,
                info.ImagePath,
                info.JsonPath,
                w,
                h,
                info.IsBase));
        }

        return result.OrderBy(a => a.Category).ThenBy(a => a.DisplayName).ToList();
    }

    private static void ScanDirectoryForExtraAssets(
        string resourcesDir,
        Dictionary<string, (string ImagePath, string? JsonPath, bool IsBase)> map,
        bool isBase)
    {
        var candidates = Directory.EnumerateFiles(resourcesDir, "*.png", SearchOption.AllDirectories);
        foreach (var file in candidates)
        {
            var rel = Path.GetRelativePath(resourcesDir, file).Replace('\\', '/');

            // Skip packed.png and wallpapers
            if (rel.Equals("sprites/packed.png", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("layout/asset/sprite/wallpaper/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var jsonCandidate = Path.ChangeExtension(file, ".json");
            var jsonPath = File.Exists(jsonCandidate) ? jsonCandidate : null;

            map[rel] = (file, jsonPath, isBase);
        }
    }

    private static string CategorizeExtraAsset(string relativePath)
    {
        var lower = relativePath.ToLowerInvariant();
        if (lower.Contains("sampleasset") || lower.Contains("mario") || lower.Contains("luigi") || lower.Contains("goku"))
        {
            return "Characters";
        }
        if (lower.Contains("layout"))
        {
            return "Layout Sprites";
        }
        if (lower.Contains("frame_") || lower.Contains("dot_num"))
        {
            return "Frames & Numbers";
        }
        return "Extra Elements";
    }

    public void AddExtraAsset(
        string themeDraftPath,
        string relativeDestinationPath,
        string sourceImagePath,
        string? sourceJsonPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDestinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);

        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Source image not found.", sourceImagePath);
        }

        using var bmp = SKBitmap.Decode(sourceImagePath)
            ?? throw new InvalidDataException("Source file is not a valid PNG image.");

        var fullDestinationPath = Path.Combine(themeDraftPath, relativeDestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestinationPath)!);

        SaveBitmapToPng(bmp, fullDestinationPath);

        var destJsonPath = Path.ChangeExtension(fullDestinationPath, ".json");
        if (sourceJsonPath != null && File.Exists(sourceJsonPath))
        {
            File.Copy(sourceJsonPath, destJsonPath, true);
        }
        else
        {
            UpdateOrGenerateTextureJson(destJsonPath, bmp.Width, bmp.Height);
        }
    }

    public void ReplaceExtraAsset(string targetImagePath, string replacementImagePath, string? jsonPath = null)
    {
        ReplaceWallpaper(targetImagePath, replacementImagePath, jsonPath);
    }

    public void DeleteExtraAsset(string imagePath, string? jsonPath = null)
    {
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        var json = jsonPath ?? Path.ChangeExtension(imagePath, ".json");
        if (File.Exists(json))
        {
            File.Delete(json);
        }
    }

    public void ExportExtraAsset(ExtraAsset asset, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(asset.ImagePath))
        {
            throw new FileNotFoundException("Asset image not found.", asset.ImagePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(asset.ImagePath, destinationPath, true);
    }

    public void ExportPackedSprite(string atlasImagePath, SpriteRegion sprite, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasImagePath);
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(atlasImagePath))
        {
            throw new FileNotFoundException("Atlas PNG image not found.", atlasImagePath);
        }

        using var atlas = SKBitmap.Decode(atlasImagePath)
            ?? throw new InvalidDataException("Cannot decode atlas PNG.");

        var frame = sprite.Frame;
        if (frame.X < 0 || frame.Y < 0 || frame.X + frame.Width > atlas.Width || frame.Y + frame.Height > atlas.Height)
        {
            throw new InvalidDataException("Sprite frame boundaries exceed atlas image dimensions.");
        }

        using var extracted = new SKBitmap(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(extracted))
        using (var paint = new SKPaint
        {
            BlendMode = SKBlendMode.Src,
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
        })
        {
            var srcRect = SKRectI.Create(frame.X, frame.Y, frame.Width, frame.Height);
            var destRect = SKRectI.Create(0, 0, frame.Width, frame.Height);
            canvas.DrawBitmap(atlas, srcRect, destRect, paint);
            canvas.Flush();
        }

        SaveBitmapToPng(extracted, destinationPath);
    }

    private static void SaveBitmapToPng(SKBitmap bitmap, string outputPath)
    {
        var temp = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(temp))
        {
            data.SaveTo(stream);
        }

        File.Move(temp, outputPath, true);
    }

    private static void UpdateOrGenerateTextureJson(string jsonPath, int width, int height)
    {
        JsonObject root;
        if (File.Exists(jsonPath))
        {
            try
            {
                var content = File.ReadAllText(jsonPath);
                root = JsonNode.Parse(content)?.AsObject() ?? [];
            }
            catch
            {
                root = [];
            }
        }
        else
        {
            root = [];
        }

        var texture = root["texture"]?.AsObject() ?? [];
        if (!texture.ContainsKey("id"))
        {
            texture["id"] = Guid.NewGuid().ToString();
        }
        texture["width"] = width;
        texture["height"] = height;
        root["texture"] = texture;

        if (!root.ContainsKey("sprites"))
        {
            root["sprites"] = new JsonObject();
        }

        var tempJson = $"{jsonPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempJson, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempJson, jsonPath, true);
    }
}
