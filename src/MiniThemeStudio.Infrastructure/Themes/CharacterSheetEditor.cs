using System.Globalization;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

// The pictures of a character sheet are found by the transparent rows and columns around them, as the console
// draws them: the script that cuts them reads fixed places, which a replaced picture keeps.
public sealed class CharacterSheetEditor : ICharacterSheetEditor
{
    private const string SheetResource = "/sprites/characters_sbm.png";
    private const byte VisibleAlpha = 16;

    public CharacterSheet? GetSheet(string themePath, string? baseUiPath)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        if (layer.Resolve(SheetResource) is not { } imagePath)
        {
            return null;
        }

        // the places come from the Base sheet: a new picture with transparent edges must not move them
        using var layout = Decode(layer.ResolveInBase(SheetResource) ?? imagePath, "The character sheet");
        return new CharacterSheet(
            imagePath,
            SheetResource.TrimStart('/'),
            layout.Width,
            layout.Height,
            layer.ResolveInTheme(SheetResource) is null,
            FindFrames(layout));
    }

    public void ReplaceFrame(string themeDraftPath, string? baseUiPath, CharacterSheetFrame frame, string pictureImagePath)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var picture = Decode(pictureImagePath, Path.GetFileName(pictureImagePath));
        if (picture.Width != frame.Width || picture.Height != frame.Height)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"The picture must be {frame.Width} × {frame.Height} pixels, like the one it replaces; {Path.GetFileName(pictureImagePath)} is {picture.Width} × {picture.Height}."));
        }

        var (layer, target) = DraftSheet(themeDraftPath, baseUiPath);
        using var sheet = Decode(target, "The character sheet");
        using (var canvas = new SKCanvas(sheet))
        {
            // the old picture goes, transparent pixels of the new one included
            using var clear = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawBitmap(picture, new SKRect(frame.X, frame.Y, frame.X + frame.Width, frame.Y + frame.Height), clear);
        }

        Save(sheet, layer.ThemeResourceFile(SheetResource));
    }

    public void ReplaceSheet(string themeDraftPath, string? baseUiPath, string sheetImagePath)
    {
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var current = layer.Resolve(SheetResource) ?? throw new FileNotFoundException("This UI has no character sheet (resources/sprites/characters_sbm.png).");
        using var replacement = Decode(sheetImagePath, Path.GetFileName(sheetImagePath));
        using var original = Decode(current, "The character sheet");
        if (replacement.Width != original.Width || replacement.Height != original.Height)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"The sheet must be {original.Width} × {original.Height} pixels, like the one it replaces; {Path.GetFileName(sheetImagePath)} is {replacement.Width} × {replacement.Height}."));
        }

        var target = layer.ThemeResourceFile(SheetResource);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(sheetImagePath, target, true);
    }

    // rows of pictures between transparent lines, then pictures between transparent columns of each row
    private static List<CharacterSheetFrame> FindFrames(SKBitmap sheet)
    {
        var frames = new List<CharacterSheetFrame>();
        var row = 0;
        foreach (var (top, bottom) in Runs(sheet.Height, y => Enumerable.Range(0, sheet.Width).Any(x => sheet.GetPixel(x, y).Alpha > VisibleAlpha)))
        {
            var index = 0;
            foreach (var (left, right) in Runs(sheet.Width, x => Enumerable.Range(top, bottom - top).Any(y => sheet.GetPixel(x, y).Alpha > VisibleAlpha)))
            {
                frames.Add(new CharacterSheetFrame(row, index++, left, top, right - left, bottom - top));
            }

            row++;
        }

        return frames;
    }

    private static IEnumerable<(int Start, int End)> Runs(int length, Func<int, bool> isInked)
    {
        var start = -1;
        for (var position = 0; position <= length; position++)
        {
            var inked = position < length && isInked(position);
            if (inked && start < 0)
            {
                start = position;
            }
            else if (!inked && start >= 0)
            {
                yield return (start, position);
                start = -1;
            }
        }
    }

    // the theme's copy of the sheet, made from the Base one before its first change
    private static (ThemeLayer Layer, string Path) DraftSheet(string themeDraftPath, string? baseUiPath)
    {
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var target = layer.ThemeResourceFile(SheetResource);
        if (!File.Exists(target))
        {
            var source = layer.Resolve(SheetResource) ?? throw new FileNotFoundException("This UI has no character sheet (resources/sprites/characters_sbm.png).");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        return (layer, target);
    }

    private static SKBitmap Decode(string path, string description)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"{description} is not a valid PNG image.");
        var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new InvalidDataException($"{description} cannot be read.");
        }

        return bitmap;
    }

    private static void Save(SKBitmap sheet, string path)
    {
        using var png = sheet.Encode(SKEncodedImageFormat.Png, 100);
        var temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        {
            png.SaveTo(file);
        }

        File.Move(temporary, path, true);
    }
}
