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

    public string EnsureDraftCopy(string themeDraftPath, string relativePath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var targetPath = EnsureInsideDraft(themeDraftPath, relativePath);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file not found.", sourcePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath);
        return targetPath;
    }

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

            list.Add(new WallpaperAsset(name, displayName, pngPath, jsonPath, width, height, isFromBase, ReadTextureId(jsonPath)));
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

    public void ReplaceWallpaper(string targetImagePath, string replacementImagePath, string? templateJsonPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementImagePath);

        if (!File.Exists(replacementImagePath))
        {
            throw new FileNotFoundException("Replacement image not found.", replacementImagePath);
        }

        using var replacementBmp = SKBitmap.Decode(replacementImagePath)
            ?? throw new InvalidDataException("Replacement file is not a valid PNG image.");

        var targetJsonPath = Path.ChangeExtension(targetImagePath, ".json");
        var textureJson = LoadTextureJson(targetJsonPath, templateJsonPath);
        ResizeTextureJson(textureJson, replacementBmp.Width, replacementBmp.Height, Path.GetFileName(targetImagePath));

        Directory.CreateDirectory(Path.GetDirectoryName(targetImagePath)!);
        SaveBitmapToPng(replacementBmp, targetImagePath);
        WriteTextureJson(targetJsonPath, textureJson);
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

        foreach (var atlasPath in map.Keys.Where(relPath => IsAtlasImage(relPath, draftResDir, baseResDir)).ToList())
        {
            map.Remove(atlasPath);
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

            var jsonPath = info.JsonPath
                ?? (baseResDir is null ? null : Path.Combine(baseResDir, Path.ChangeExtension(relPath, ".json")) is var baseJson && File.Exists(baseJson) ? baseJson : null);
            result.Add(new ExtraAsset(
                relPath,
                displayName,
                category,
                info.ImagePath,
                jsonPath,
                w,
                h,
                info.IsBase,
                ReadTextureId(jsonPath)));
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

            if (rel.StartsWith("layout/asset/sprite/wallpaper/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var jsonCandidate = Path.ChangeExtension(file, ".json");
            var jsonPath = File.Exists(jsonCandidate) ? jsonCandidate : null;

            map[rel] = (file, jsonPath, isBase);
        }
    }

    private static bool IsAtlasImage(string relativePath, string draftResourcesPath, string? baseResourcesPath)
    {
        if (!relativePath.StartsWith("sprites/", StringComparison.OrdinalIgnoreCase)
            || relativePath.IndexOf('/', "sprites/".Length) >= 0)
        {
            return false;
        }

        var jsonRelativePath = Path.ChangeExtension(relativePath, ".json");
        return TextureAtlasReader.IsPrimaryAtlasName(Path.GetFileNameWithoutExtension(relativePath))
            || new[] { draftResourcesPath, baseResourcesPath }
                .Where(root => root != null)
                .Select(root => Path.Combine(root!, jsonRelativePath))
                .Any(jsonPath => File.Exists(jsonPath) && TextureAtlasReader.IsAtlasDefinition(jsonPath));
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
            var textureJson = LoadTextureJson(destJsonPath, null);
            ResizeTextureJson(textureJson, bmp.Width, bmp.Height, Path.GetFileName(fullDestinationPath));
            WriteTextureJson(destJsonPath, textureJson);
        }
    }

    public void ReplaceExtraAsset(string targetImagePath, string replacementImagePath, string? templateJsonPath = null)
    {
        ReplaceWallpaper(targetImagePath, replacementImagePath, templateJsonPath);
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

    public void ProvideMissingTexture(string themeDraftPath, MissingTexture texture, string sourceImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(texture);

        using var bitmap = SKBitmap.Decode(sourceImagePath)
            ?? throw new InvalidDataException("Source file is not a valid PNG image.");
        var imagePath = EnsureInsideDraft(themeDraftPath, texture.ImageRelativePath);
        var definitionPath = EnsureInsideDraft(themeDraftPath, texture.DefinitionRelativePath);
        if (File.Exists(imagePath))
        {
            throw new IOException($"The texture image already exists: {texture.ImageRelativePath}");
        }

        var definition = File.Exists(definitionPath)
            ? LoadTextureJson(definitionPath, null)
            : new JsonObject
            {
                ["texture"] = new JsonObject(),
                ["sprites"] = new JsonObject
                {
                    ["Default"] = new JsonObject
                    {
                        ["textureFrame"] = new JsonArray(0, 0, bitmap.Width, bitmap.Height),
                        ["pivot"] = new JsonArray(Half(bitmap.Width), Half(bitmap.Height)),
                        ["sourceSize"] = new JsonArray(bitmap.Width, bitmap.Height),
                        ["sourceOffset"] = new JsonArray(0, 0),
                    },
                },
            };
        var textureNode = (JsonObject)definition["texture"]!;
        if (textureNode["id"]?.GetValue<string>() is { } existingId && existingId != texture.TextureId)
        {
            throw new InvalidDataException($"{texture.DefinitionRelativePath} belongs to texture {existingId}, not {texture.TextureId}.");
        }

        textureNode["id"] = texture.TextureId;
        ResizeTextureJson(definition, bitmap.Width, bitmap.Height, Path.GetFileName(imagePath));

        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        SaveBitmapToPng(bitmap, imagePath);
        WriteTextureJson(definitionPath, definition);
    }

    private static JsonValue Half(int size)
    {
        return size % 2 == 0 ? JsonValue.Create(size / 2) : JsonValue.Create(size / 2.0);
    }

    private static string EnsureInsideDraft(string themeDraftPath, string relativePath)
    {
        var draftRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(themeDraftPath));
        var targetPath = Path.GetFullPath(Path.Combine(draftRoot, relativePath));
        if (!targetPath.StartsWith(draftRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The path must stay inside the theme draft: {relativePath}", nameof(relativePath));
        }

        return targetPath;
    }

    private static string? ReadTextureId(string? jsonPath)
    {
        if (jsonPath is null || !File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            return (JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject)?["texture"]?["id"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static JsonObject LoadTextureJson(string targetJsonPath, string? templateJsonPath)
    {
        var sourcePath = File.Exists(targetJsonPath)
            ? targetJsonPath
            : templateJsonPath != null && File.Exists(templateJsonPath)
                ? templateJsonPath
                : null;

        if (sourcePath is null)
        {
            return new JsonObject
            {
                ["texture"] = new JsonObject { ["id"] = Guid.NewGuid().ToString() },
                ["sprites"] = new JsonObject(),
            };
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(sourcePath)) as JsonObject
                ?? throw new InvalidDataException($"Texture definition is not a JSON object: {sourcePath}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid texture definition: {sourcePath}", exception);
        }
    }

    private static void ResizeTextureJson(JsonObject root, int width, int height, string imageName)
    {
        var texture = root["texture"] as JsonObject ?? [];
        root["texture"] = texture;
        if (!texture.ContainsKey("id"))
        {
            texture["id"] = Guid.NewGuid().ToString();
        }

        var oldWidth = texture["width"]?.GetValue<int>() ?? 0;
        var oldHeight = texture["height"]?.GetValue<int>() ?? 0;
        texture["width"] = width;
        texture["height"] = height;

        if (root["sprites"] is not JsonObject sprites)
        {
            root["sprites"] = new JsonObject();
            return;
        }

        if ((oldWidth == width && oldHeight == height) || oldWidth == 0 || oldHeight == 0)
        {
            return;
        }

        foreach (var (_, node) in sprites)
        {
            var frame = (node?["textureFrame"] as JsonArray)?.Select(value => value?.GetValue<int>() ?? -1).ToArray();
            if (frame is not [0, 0, var frameWidth, var frameHeight] || frameWidth != oldWidth || frameHeight != oldHeight)
            {
                throw new InvalidDataException(
                    $"{imageName} contains sub-sprites, so the replacement must keep {oldWidth} x {oldHeight} pixels "
                    + $"(got {width} x {height}).");
            }
        }

        foreach (var (_, node) in sprites)
        {
            var sprite = (JsonObject)node!;
            sprite["textureFrame"] = new JsonArray(0, 0, width, height);
            sprite["sourceSize"] = new JsonArray(width, height);
            if (sprite["pivot"] is JsonArray { Count: 2 } pivot)
            {
                sprite["pivot"] = new JsonArray(
                    ScaleCoordinate(pivot[0], width, oldWidth),
                    ScaleCoordinate(pivot[1], height, oldHeight));
            }
        }
    }

    private static JsonValue ScaleCoordinate(JsonNode? value, int newSize, int oldSize)
    {
        var scaled = (value?.GetValue<double>() ?? 0) * newSize / oldSize;
        return scaled == Math.Floor(scaled) ? JsonValue.Create((int)scaled) : JsonValue.Create(scaled);
    }

    private static void WriteTextureJson(string jsonPath, JsonObject root)
    {
        var tempJson = $"{jsonPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempJson, EngineJson.ToCompactString(root));
        File.Move(tempJson, jsonPath, true);
    }

    public IReadOnlyList<MiniThemeStudio.Core.Fonts.FontAsset> GetFonts(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var draftFontsDir = Path.Combine(themeDraftPath, "resources", "fonts");
        var baseFontsDir = baseUiPath != null ? Path.Combine(baseUiPath, "resources", "fonts") : null;

        var fontFiles = new Dictionary<string, (string Path, string Extension, bool FromBase)>(StringComparer.OrdinalIgnoreCase);

        if (baseFontsDir != null && Directory.Exists(baseFontsDir))
        {
            foreach (var file in Directory.EnumerateFiles(baseFontsDir, "*.*"))
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".fnt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".font", StringComparison.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!MiniThemeStudio.Infrastructure.Fonts.FirmwareFonts.IsFirmwareOnly(name))
                    {
                        fontFiles[name] = (file, ext, true);
                    }
                }
            }
        }

        if (Directory.Exists(draftFontsDir))
        {
            foreach (var file in Directory.EnumerateFiles(draftFontsDir, "*.*"))
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".fnt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".font", StringComparison.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!MiniThemeStudio.Infrastructure.Fonts.FirmwareFonts.IsFirmwareOnly(name))
                    {
                        fontFiles[name] = (file, ext, false);
                    }
                }
            }
        }

        var result = new List<MiniThemeStudio.Core.Fonts.FontAsset>();
        foreach (var (name, (filePath, ext, fromBase)) in fontFiles.OrderBy(kvp => GetFontSortKey(kvp.Key)))
        {
            try
            {
                var font = MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(filePath);
                var relPath = $"resources/fonts/{name}{ext}";
                var (displayName, roles) = GetFontMetadata(name);

                result.Add(new MiniThemeStudio.Core.Fonts.FontAsset(
                    Name: name,
                    DisplayName: displayName,
                    FilePath: filePath,
                    RelativePath: relPath,
                    GlyphCount: font.Glyphs.Count,
                    TextureWidth: font.Common.ScaleW,
                    TextureHeight: font.Common.ScaleH,
                    LineHeight: font.Common.LineHeight,
                    IsFromBaseUi: fromBase)
                {
                    Roles = roles
                });
            }
            catch
            {
                // If corrupted or unreadable, still register with fallback metrics
                var relPath = $"resources/fonts/{name}{ext}";
                var (displayName, roles) = GetFontMetadata(name);
                result.Add(new MiniThemeStudio.Core.Fonts.FontAsset(
                    Name: name,
                    DisplayName: displayName,
                    FilePath: filePath,
                    RelativePath: relPath,
                    GlyphCount: 0,
                    TextureWidth: 0,
                    TextureHeight: 0,
                    LineHeight: 0,
                    IsFromBaseUi: fromBase)
                {
                    Roles = roles
                });
            }
        }

        return result;
    }

    public MiniThemeStudio.Core.Fonts.MiniFont LoadFont(string fontFilePath)
    {
        return MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(fontFilePath);
    }

    public void ReplaceFontTexture(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, string replacementPngPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPngPath);

        var draftPath = EnsureDraftCopy(themeDraftPath, font.RelativePath, font.FilePath);
        var miniFont = MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(draftPath);

        var newPngBytes = File.ReadAllBytes(replacementPngPath);
        using var bmp = SKBitmap.Decode(newPngBytes);
        if (bmp == null)
        {
            throw new InvalidOperationException("The replacement image is not a valid PNG.");
        }

        miniFont.TexturePngBytes = newPngBytes;
        miniFont.Common = miniFont.Common with
        {
            ScaleW = bmp.Width,
            ScaleH = bmp.Height
        };

        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(miniFont, draftPath);
    }

    public void ReplaceFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, string sourceFontPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFontPath);

        var targetPath = EnsureInsideDraft(themeDraftPath, font.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        // Validate that source is a readable font before overwriting
        _ = MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(sourceFontPath);
        File.Copy(sourceFontPath, targetPath, overwrite: true);
    }

    public void ExportFontTexture(MiniThemeStudio.Core.Fonts.FontAsset font, string destinationPngPath)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPngPath);

        var miniFont = MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(font.FilePath);
        var dir = Path.GetDirectoryName(destinationPngPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(destinationPngPath, miniFont.TexturePngBytes);
    }

    public void ExportFont(MiniThemeStudio.Core.Fonts.FontAsset font, string destinationFontPath)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFontPath);

        var dir = Path.GetDirectoryName(destinationFontPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.Copy(font.FilePath, destinationFontPath, overwrite: true);
    }

    public void ImportTrueTypeFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset targetFont, string ttfOrZipPath, int? fontSize = null, bool antiAlias = false,
        MiniThemeStudio.Core.Fonts.ExtraScripts extraScripts = MiniThemeStudio.Core.Fonts.ExtraScripts.None, string? fillFontPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(targetFont);
        ArgumentException.ThrowIfNullOrWhiteSpace(ttfOrZipPath);

        var size = fontSize.HasValue && fontSize.Value > 0
            ? fontSize.Value
            : (targetFont.LineHeight > 0 ? targetFont.LineHeight : 16);

        var rasterizer = new MiniThemeStudio.Infrastructure.Fonts.TrueTypeFontRasterizer();
        var miniFont = rasterizer.Rasterize(ttfOrZipPath, new MiniThemeStudio.Core.Fonts.TrueTypeRasterOptions(
            FontSize: size,
            AntiAlias: antiAlias,
            ExtraScripts: extraScripts,
            FillFontPath: fillFontPath));

        var targetPath = EnsureInsideDraft(themeDraftPath, targetFont.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(miniFont, targetPath);
    }

    public void CompleteFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, MiniThemeStudio.Core.Fonts.ExtraScripts extraScripts, string? fillFontPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(font);

        var completed = MiniThemeStudio.Infrastructure.Fonts.MiniFontCompleter.Complete(
            MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(font.FilePath), extraScripts, fillFontPath);

        var targetPath = EnsureInsideDraft(themeDraftPath, font.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(completed, targetPath);
    }

    private static (string DisplayName, string[] Roles) GetFontMetadata(string name) =>
        name.ToLowerInvariant() switch
        {
            "l" or "large" => ($"Large UI Font ({name})", ["Menus", "Dialog Headers", "Action Prompts"]),
            "m" or "medium" => ($"Medium UI Font ({name})", ["Game Settings", "Options Menu", "Subtitles"]),
            "s" or "small" => ($"Small UI Font ({name})", ["Footer Hints", "Button Labels", "Metadata"]),
            "title" => ("Game Title Font (title)", ["Game Titles in Carousel"]),
            "copyright" => ("Copyright Font (copyright)", ["Boot Screen Copyright Text"]),
            "debug" => ("Debug Console Font (debug)", ["System Debug Messages"]),
            _ => ($"{name} Font", ["UI Font"])
        };

    private static int GetFontSortKey(string name) =>
        name.ToLowerInvariant() switch
        {
            "title" => 0,
            "l" or "large" => 1,
            "m" or "medium" => 2,
            "s" or "small" => 3,
            _ => 10
        };
}
