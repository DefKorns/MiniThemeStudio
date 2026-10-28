using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Audio;
using MiniThemeStudio.Infrastructure.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeValidator(IThemeResourceIndexer? resourceIndexer = null) : IThemeValidator
{
    private readonly IThemeResourceIndexer _resourceIndexer = resourceIndexer ?? new ThemeResourceIndexer();

    public ThemeValidationResult Validate(string themeDraftPath, string? baseUiPath = null, ThemeValidationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        options ??= new ThemeValidationOptions();
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

        // Validate fonts if present
        foreach (var fontsDir in new[] { Path.Combine(themeDraftPath, ThemeFonts.Folder), Path.Combine(themeDraftPath, "resources", "fonts") })
        {
            if (Directory.Exists(fontsDir))
            {
                ValidateFonts(fontsDir, issues);
            }
        }

        // Validate sounds / BGM if present
        var soundsDir = Path.Combine(themeDraftPath, "resources", "sounds");
        if (Directory.Exists(soundsDir))
        {
            if (options.IncludeBgm)
            {
                ValidateSounds(soundsDir, issues, options);
            }
            else
            {
                var bgmFiles = Directory.EnumerateFiles(soundsDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileName(f).StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (bgmFiles.Count > 0)
                {
                    issues.Add(new ThemeValidationIssue("Custom BGM audio is excluded from export (stock console audio will be preserved).", false));
                }
            }
        }

        // Check if delta package
        var initJson = Path.Combine(themeDraftPath, "init.json");
        var depsJson = Path.Combine(themeDraftPath, "dependencies.json");
        if (!File.Exists(initJson) || !File.Exists(depsJson))
        {
            issues.Add(new ThemeValidationIssue("Theme is an incomplete delta mod. System files will be inherited from the console stock firmware.", false));
        }

        issues.AddRange(_resourceIndexer.Analyze(themeDraftPath, baseUiPath).Issues);

        var isValid = issues.TrueForAll(i => !i.IsError);
        return new ThemeValidationResult(isValid, issues);
    }

    private static void ValidateFonts(string fontsDir, List<ThemeValidationIssue> issues)
    {
        foreach (var file in Directory.EnumerateFiles(fontsDir, "*.*"))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".fnt", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".font", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fontName = Path.GetFileNameWithoutExtension(file);
            if (FirmwareFonts.IsFirmwareOnly(fontName))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            try
            {
                var font = MiniFontReader.Read(file);
                var validation = FontValidator.Validate(font);

                if (!validation.HasBasicAscii)
                {
                    issues.Add(new ThemeValidationIssue(
                        $"Font '{fileName}' has a subsetted character set ({validation.MissingAsciiChars.Count} basic ASCII characters omitted).",
                        false));
                }
                else if (!validation.HasLatinAccents)
                {
                    issues.Add(new ThemeValidationIssue(
                        $"Font '{fileName}' is missing European accented glyphs ({validation.MissingAccentChars.Count} missing, e.g. {string.Join(", ", validation.MissingAccentChars.Take(6))}). Potential C8 error on translated UI.",
                        false));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ThemeValidationIssue($"Font file corrupt: {fileName} ({ex.Message})", true));
            }
        }
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
                $"{atlas.Name}.png is {bmp.Width}x{bmp.Height} but {atlas.Name}.json declares {atlas.Size.Width}x{atlas.Size.Height}.",
                false));
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

    private static void ValidateSounds(string soundsDir, List<ThemeValidationIssue> issues, ThemeValidationOptions options)
    {
        var soundFiles = Directory.EnumerateFiles(soundsDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var ffmpegConfigured = !string.IsNullOrWhiteSpace(options.FFmpegPath) && File.Exists(options.FFmpegPath);

        foreach (var file in soundFiles)
        {
            var fileName = Path.GetFileName(file);
            var isBgm = true;

            if (!BgmAudioValidator.IsSupportedAudioFile(file))
            {
                continue;
            }

            var validation = BgmAudioValidator.Validate(file, isBgm ? options.BgmChannels : null);

            if (validation.IsValid)
            {
                foreach (var warning in validation.Warnings)
                {
                    issues.Add(new ThemeValidationIssue(warning, false));
                }
                continue;
            }

            if (isBgm && options.ConvertBgmAudio)
            {
                if (!ffmpegConfigured)
                {
                    issues.Add(new ThemeValidationIssue(
                        $"Convert BGM audio is enabled for '{fileName}', but FFmpeg is not configured in Settings.",
                        true));
                }
                else
                {
                    issues.Add(new ThemeValidationIssue(
                        $"'{fileName}' will be converted to PCM 16-bit 44.1 kHz {(options.BgmChannels == Core.Audio.BgmAudioChannels.Mono ? "mono" : "stereo")} WAV using FFmpeg.",
                        false));
                }
            }
            else
            {
                foreach (var error in validation.Errors)
                {
                    issues.Add(new ThemeValidationIssue(error, true));
                }
                foreach (var warning in validation.Warnings)
                {
                    issues.Add(new ThemeValidationIssue(warning, false));
                }
            }
        }
    }
}
