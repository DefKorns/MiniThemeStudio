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

        var targetPath = ThemeLayer.InsideRoot(themeDraftPath, relativePath);
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

        const string WallpaperFolder = "layout/asset/sprite/wallpaper";
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var allNames = layer.ListFiles(WallpaperFolder, "*.png", recursive: false)
            .Select(file => Path.GetFileNameWithoutExtension(file.ResourcePath))
            .Concat(CoreWallpaperNames) // the core wallpapers are listed even when missing
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = new List<WallpaperAsset>();
        foreach (var name in allNames.OrderBy(GetWallpaperSortKey))
        {
            var pngResource = $"/{WallpaperFolder}/{name}.png";
            var pngPath = layer.Resolve(pngResource) ?? layer.ThemeResourceFile(pngResource);
            var jsonPath = layer.Resolve($"/{WallpaperFolder}/{name}.json");
            var isFromBase = layer.ResolveInTheme(pngResource) is null && layer.ResolveInBase(pngResource) is not null;

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
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);

        var result = new List<ExtraAsset>();
        foreach (var image in layer.ListFiles("", "*.png"))
        {
            var relPath = image.ResourcePath.TrimStart('/');
            if (relPath.StartsWith("layout/asset/sprite/wallpaper/", StringComparison.OrdinalIgnoreCase)
                || IsAtlasImage(relPath, draftResDir, baseResDir))
            {
                continue;
            }

            int w = 0, h = 0;
            using (var bmp = SKBitmap.Decode(image.FilePath))
            {
                if (bmp != null)
                {
                    w = bmp.Width;
                    h = bmp.Height;
                }
            }

            var jsonPath = layer.Resolve(Path.ChangeExtension(image.ResourcePath, ".json"));
            result.Add(new ExtraAsset(
                relPath,
                Path.GetFileNameWithoutExtension(relPath),
                image.FilePath,
                jsonPath,
                w,
                h,
                image.IsFromBase,
                ReadTextureId(jsonPath),
                image.OverridesBase));
        }

        return result.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
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
        var imagePath = ThemeLayer.InsideRoot(themeDraftPath, texture.ImageRelativePath);
        var definitionPath = ThemeLayer.InsideRoot(themeDraftPath, texture.DefinitionRelativePath);
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

        var fontFiles = new Dictionary<string, (string Path, string Extension, bool FromBase)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new ThemeLayer(themeDraftPath, baseUiPath).ListFiles("fonts", "*.*", recursive: false))
        {
            var ext = Path.GetExtension(file.FilePath);
            var name = Path.GetFileNameWithoutExtension(file.FilePath);
            if ((ext.Equals(".fnt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".font", StringComparison.OrdinalIgnoreCase))
                && !MiniThemeStudio.Infrastructure.Fonts.FirmwareFonts.IsFirmwareOnly(name))
            {
                // a theme font named like a Base font with the other extension replaces it
                if (!fontFiles.TryGetValue(name, out var existing) || existing.FromBase)
                {
                    fontFiles[name] = (file.FilePath, ext, file.IsFromBase);
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
            throw new InvalidDataException("The replacement image is not a valid PNG.");
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

        var targetPath = ThemeLayer.InsideRoot(themeDraftPath, font.RelativePath);
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

        var targetPath = ThemeLayer.InsideRoot(themeDraftPath, targetFont.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(miniFont, targetPath);
    }

    public void CompleteFont(string themeDraftPath, MiniThemeStudio.Core.Fonts.FontAsset font, MiniThemeStudio.Core.Fonts.ExtraScripts extraScripts, string? fillFontPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(font);

        var completed = MiniThemeStudio.Infrastructure.Fonts.MiniFontCompleter.Complete(
            MiniThemeStudio.Infrastructure.Fonts.MiniFontReader.Read(font.FilePath), extraScripts, fillFontPath);

        var targetPath = ThemeLayer.InsideRoot(themeDraftPath, font.RelativePath);
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

    public IReadOnlyList<MiniThemeStudio.Core.Audio.BgmTrack> GetBgmTracks(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var soundSubdir = layer.FirstSubfolder("sounds") ?? "hvc";
        var trackNames = new[]
        {
            ("bgm_home", "Home Menu Music (bgm_home)"),
            ("bgm_boot", "Boot Music (bgm_boot)")
        };

        var tracks = new List<MiniThemeStudio.Core.Audio.BgmTrack>();

        foreach (var (name, displayName) in trackNames)
        {
            var relativePath = $"resources/sounds/{soundSubdir}/{name}.wav";
            var draftFile = layer.FindFiles($"sounds/{soundSubdir}", $"{name}.*", inBase: false)
                .FirstOrDefault(Audio.BgmAudioValidator.IsSupportedAudioFile);
            var baseFile = layer.FindFiles($"sounds/{soundSubdir}", $"{name}.*", inBase: true)
                .FirstOrDefault(Audio.BgmAudioValidator.IsSupportedAudioFile);

            MiniThemeStudio.Core.Audio.WavAudioInfo? audioInfo = null;
            string statusDesc;

            var canMute = name.Equals("bgm_boot", StringComparison.OrdinalIgnoreCase);
            var isMuted = false;

            if (draftFile != null)
            {
                var ext = Path.GetExtension(draftFile);
                if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    audioInfo = Audio.WavAudioReader.Read(draftFile);
                    isMuted = Audio.BgmAudioValidator.IsSilentPcm(draftFile);
                    statusDesc = isMuted
                        ? "Draft: Muted (Silence)"
                        : $"Draft: {audioInfo.FormatDescription}";
                }
                else
                {
                    statusDesc = $"Draft: {Path.GetFileName(draftFile)} ({ext.ToUpperInvariant()}, needs conversion)";
                }
            }
            else if (baseFile != null)
            {
                var ext = Path.GetExtension(baseFile);
                if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    audioInfo = Audio.WavAudioReader.Read(baseFile);
                    statusDesc = $"Base: {audioInfo.FormatDescription}";
                }
                else
                {
                    statusDesc = $"Base: {Path.GetFileName(baseFile)}";
                }
            }
            else
            {
                statusDesc = "Not present in theme or base";
            }

            tracks.Add(new MiniThemeStudio.Core.Audio.BgmTrack(
                Name: name,
                DisplayName: displayName,
                RelativePath: relativePath,
                DraftFilePath: draftFile,
                BaseFilePath: baseFile,
                ExistsInDraft: draftFile != null,
                ExistsInBase: baseFile != null,
                AudioInfo: audioInfo,
                StatusDescription: statusDesc,
                CanMute: canMute,
                IsMuted: isMuted));
        }

        return tracks;
    }

    public void ReplaceBgmTrack(string themeDraftPath, string trackName, string sourceAudioFilePath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAudioFilePath);

        if (!File.Exists(sourceAudioFilePath))
        {
            throw new FileNotFoundException("Source audio file not found.", sourceAudioFilePath);
        }

        var soundSubdir = new ThemeLayer(themeDraftPath, baseUiPath).FirstSubfolder("sounds") ?? "hvc";
        var draftDir = Path.Combine(themeDraftPath, "resources", "sounds", soundSubdir);
        Directory.CreateDirectory(draftDir);

        var ext = Path.GetExtension(sourceAudioFilePath).ToLowerInvariant();
        var targetPath = Path.Combine(draftDir, $"{trackName}{ext}");

        foreach (var existing in Directory.EnumerateFiles(draftDir, $"{trackName}.*"))
        {
            if (!existing.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(existing);
            }
        }

        File.Copy(sourceAudioFilePath, targetPath, overwrite: true);
    }

    public void MuteBgmTrack(string themeDraftPath, string trackName, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackName);

        var soundSubdir = new ThemeLayer(themeDraftPath, baseUiPath).FirstSubfolder("sounds") ?? "hvc";
        var draftDir = Path.Combine(themeDraftPath, "resources", "sounds", soundSubdir);
        Directory.CreateDirectory(draftDir);

        foreach (var existing in Directory.EnumerateFiles(draftDir, $"{trackName}.*"))
        {
            File.Delete(existing);
        }

        var silentWav = Audio.WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 4410);
        var targetPath = Path.Combine(draftDir, $"{trackName}.wav");
        File.WriteAllBytes(targetPath, silentWav);
    }

    public void RevertBgmTrack(string themeDraftPath, string trackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackName);

        var soundsDir = Path.Combine(themeDraftPath, "resources", "sounds");
        if (!Directory.Exists(soundsDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(soundsDir, $"{trackName}.*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

}
