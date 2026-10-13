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

        IReadOnlyList<TextureAtlas> atlases;
        try
        {
            atlases = new TextureAtlasReader().ReadAll(themeDraftPath, baseUiPath);
        }
        catch (InvalidDataException exception)
        {
            atlases = [];
            issues.Add(new ThemeValidationIssue(exception.Message, true));
        }

        if (atlases.Count == 0)
        {
            issues.Add(new ThemeValidationIssue("No sprite atlas found (resources/sprites/packed.json, nes.json or hvc.json).", true));
        }

        var themeSpritesPath = Path.GetFullPath(Path.Combine(themeDraftPath, "resources", "sprites"));
        foreach (var atlas in atlases)
        {
            if (!Path.GetFullPath(atlas.DefinitionPath).StartsWith(themeSpritesPath, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ThemeValidationIssue($"{atlas.Name}.json missing in theme: resolving from Base UI.", false));
            }

            if (File.Exists(atlas.ImagePath))
            {
                ValidateAtlas(atlas, issues);
            }
            else
            {
                issues.Add(new ThemeValidationIssue($"Missing atlas image: resources/sprites/{atlas.Name}.png", true));
            }
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

    private static void ValidateAtlas(TextureAtlas atlas, List<ThemeValidationIssue> issues)
    {
        using var bmp = SKBitmap.Decode(atlas.ImagePath);
        if (bmp == null)
        {
            issues.Add(new ThemeValidationIssue($"{atlas.Name}.png is corrupt or not a valid image.", true));
            return;
        }

        if (atlas.Size.Width != bmp.Width || atlas.Size.Height != bmp.Height)
        {
            issues.Add(new ThemeValidationIssue(
                $"{atlas.Name}.png dimensions ({bmp.Width}x{bmp.Height}) do not match {atlas.Name}.json ({atlas.Size.Width}x{atlas.Size.Height}).",
                true));
        }

        foreach (var sprite in atlas.Sprites)
        {
            var frame = sprite.Frame;
            if (frame.X < 0 || frame.Y < 0 || frame.X + frame.Width > bmp.Width || frame.Y + frame.Height > bmp.Height)
            {
                issues.Add(new ThemeValidationIssue(
                    $"Sprite '{sprite.Name}' in {atlas.Name} frame [{frame.X},{frame.Y},{frame.Width},{frame.Height}] exceeds atlas dimensions ({bmp.Width}x{bmp.Height}).",
                    true));
            }
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
