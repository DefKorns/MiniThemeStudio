using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeValidator : IThemeValidator
{
    public ThemeValidationResult Validate(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        var issues = new List<ThemeValidationIssue>();

        if (!Directory.Exists(themeDraftPath))
        {
            issues.Add(new ThemeValidationIssue("Theme draft directory not found.", true));
            return new ThemeValidationResult(false, issues);
        }

        var packedPngPath = Path.Combine(themeDraftPath, "resources", "sprites", "packed.png");
        var packedJsonPath = Path.Combine(themeDraftPath, "resources", "sprites", "packed.json");

        if (!File.Exists(packedJsonPath) && baseUiPath != null)
        {
            var fallbackJson = Path.Combine(baseUiPath, "resources", "sprites", "packed.json");
            if (File.Exists(fallbackJson))
            {
                packedJsonPath = fallbackJson;
                issues.Add(new ThemeValidationIssue("packed.json missing in theme: resolving from Base UI.", false));
            }
        }

        if (!File.Exists(packedPngPath) && baseUiPath != null)
        {
            var fallbackPng = Path.Combine(baseUiPath, "resources", "sprites", "packed.png");
            if (File.Exists(fallbackPng))
            {
                packedPngPath = fallbackPng;
                issues.Add(new ThemeValidationIssue("packed.png missing in theme: resolving from Base UI.", false));
            }
        }

        if (!File.Exists(packedPngPath))
        {
            issues.Add(new ThemeValidationIssue("Missing required packed atlas image: resources/sprites/packed.png", true));
        }

        if (!File.Exists(packedJsonPath))
        {
            issues.Add(new ThemeValidationIssue("Missing atlas coordinates definition: resources/sprites/packed.json", true));
        }

        if (File.Exists(packedPngPath) && File.Exists(packedJsonPath))
        {
            ValidateAtlasAndSprites(packedPngPath, packedJsonPath, issues);
        }

        // Validate wallpapers if present
        var wallpaperDir = Path.Combine(themeDraftPath, "resources", "layout", "asset", "sprite", "wallpaper");
        if (Directory.Exists(wallpaperDir))
        {
            ValidateWallpapers(wallpaperDir, issues);
        }

        // Check if delta package
        var initJson = Path.Combine(themeDraftPath, "init.json");
        var depsJson = Path.Combine(themeDraftPath, "dependencies.json");
        if (!File.Exists(initJson) || !File.Exists(depsJson))
        {
            issues.Add(new ThemeValidationIssue("Theme is an incomplete delta mod. System files will be inherited from the console stock firmware.", false));
        }

        var isValid = issues.TrueForAll(i => !i.IsError);
        return new ThemeValidationResult(isValid, issues);
    }

    private static void ValidateAtlasAndSprites(
        string packedPngPath,
        string packedJsonPath,
        List<ThemeValidationIssue> issues)
    {
        SKBitmap? bmp = null;
        try
        {
            bmp = SKBitmap.Decode(packedPngPath);
            if (bmp == null)
            {
                issues.Add(new ThemeValidationIssue("packed.png is corrupt or not a valid image.", true));
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(packedJsonPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("texture", out var texture))
            {
                issues.Add(new ThemeValidationIssue("packed.json is missing 'texture' element.", true));
                return;
            }

            var jsonWidth = texture.GetProperty("width").GetInt32();
            var jsonHeight = texture.GetProperty("height").GetInt32();

            if (jsonWidth != bmp.Width || jsonHeight != bmp.Height)
            {
                issues.Add(new ThemeValidationIssue(
                    $"packed.png dimensions ({bmp.Width}x{bmp.Height}) do not match packed.json ({jsonWidth}x{jsonHeight}).",
                    true));
            }

            if (root.TryGetProperty("sprites", out var sprites))
            {
                var spriteCount = 0;
                foreach (var prop in sprites.EnumerateObject())
                {
                    spriteCount++;
                    var sprite = prop.Value;
                    if (sprite.TryGetProperty("textureFrame", out var tf))
                    {
                        var frame = tf.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                        if (frame.Length == 4)
                        {
                            var x = frame[0];
                            var y = frame[1];
                            var w = frame[2];
                            var h = frame[3];

                            if (x < 0 || y < 0 || x + w > bmp.Width || y + h > bmp.Height)
                            {
                                issues.Add(new ThemeValidationIssue(
                                    $"Sprite '{prop.Name}' frame [{x},{y},{w},{h}] exceeds atlas dimensions ({bmp.Width}x{bmp.Height}).",
                                    true));
                            }
                        }
                    }
                }

                if (spriteCount == 0)
                {
                    issues.Add(new ThemeValidationIssue("packed.json contains 0 sprites.", false));
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add(new ThemeValidationIssue($"Failed to validate texture atlas: {ex.Message}", true));
        }
        finally
        {
            bmp?.Dispose();
        }
    }

    private static void ValidateWallpapers(string wallpaperDir, List<ThemeValidationIssue> issues)
    {
        foreach (var png in Directory.EnumerateFiles(wallpaperDir, "*.png"))
        {
            try
            {
                using var bmp = SKBitmap.Decode(png);
                if (bmp == null)
                {
                    issues.Add(new ThemeValidationIssue($"Wallpaper image corrupt: {Path.GetFileName(png)}", true));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ThemeValidationIssue($"Failed to read wallpaper {Path.GetFileName(png)}: {ex.Message}", true));
            }
        }
    }
}
