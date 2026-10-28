using System.Globalization;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

// The pictures of a character sheet are found by the transparent rows and columns around them, as the console
// draws them: the script that cuts them reads fixed places, which a replaced picture keeps.
public sealed class CharacterSheetEditor : ICharacterSheetEditor
{
    private const byte VisibleAlpha = 16;

    // the side of a NES sprite: the room a picture has, unless the pictures beside it leave less
    private const int SpriteSide = 16;

    // the menu's characters, then the small ones of the background
    private static readonly string[] SheetNames = ["characters_sbm", "wallpaper-hvc"];

    public IReadOnlyList<CharacterSheet> GetSheets(string themePath, string? baseUiPath)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        var sheets = new List<CharacterSheet>();
        foreach (var name in SheetNames)
        {
            var resource = Resource(name);
            if (layer.Resolve(resource) is not { } imagePath)
            {
                continue;
            }

            // the places come from the Base sheet: a new picture with transparent edges must not move them
            using var layout = Decode(layer.ResolveInBase(resource) ?? imagePath, "The character sheet");
            sheets.Add(new CharacterSheet(
                name,
                imagePath,
                resource.TrimStart('/'),
                layout.Width,
                layout.Height,
                layer.ResolveInTheme(resource) is null,
                FindFrames(layout)));
        }

        return sheets;
    }

    public double ReplaceFrame(string themeDraftPath, string? baseUiPath, CharacterSheet sheet, CharacterSheetFrame frame, string pictureImagePath)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(frame);
        var resource = Resource(sheet.Name);
        using var file = Decode(pictureImagePath, Path.GetFileName(pictureImagePath));
        var ink = Ink(file) ?? throw new InvalidDataException($"{Path.GetFileName(pictureImagePath)} is empty.");
        using var inked = new SKBitmap();
        file.ExtractSubset(inked, ink);
        var (picture, scale) = Fit(inked, frame.SlotWidth, frame.SlotHeight);
        using var placed = picture;

        // centred where the Base picture was and standing where it stood, inside the slot
        var x = Math.Clamp(frame.X + ((frame.Width - placed.Width) / 2), frame.SlotX, frame.SlotX + frame.SlotWidth - placed.Width);
        var y = Math.Clamp(frame.Y + frame.Height - placed.Height, frame.SlotY, frame.SlotY + frame.SlotHeight - placed.Height);

        var (layer, target) = DraftSheet(themeDraftPath, baseUiPath, resource);
        using var pixels = Decode(target, "The character sheet");
        using (var canvas = new SKCanvas(pixels))
        {
            // the old picture goes with its whole slot
            using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
            canvas.DrawRect(new SKRect(frame.SlotX, frame.SlotY, frame.SlotX + frame.SlotWidth, frame.SlotY + frame.SlotHeight), clear);
            using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawBitmap(placed, x, y, copy);
        }

        Save(pixels, layer.ThemeResourceFile(resource));
        return scale;
    }

    public void ReplaceSheet(string themeDraftPath, string? baseUiPath, CharacterSheet sheet, string sheetImagePath)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var resource = Resource(sheet.Name);
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var current = layer.Resolve(resource) ?? throw new FileNotFoundException($"This UI has no resources{resource}.");
        using var replacement = Decode(sheetImagePath, Path.GetFileName(sheetImagePath));
        using var original = Decode(current, "The character sheet");
        if (replacement.Width != original.Width || replacement.Height != original.Height)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"The sheet must be {original.Width} × {original.Height} pixels, like the one it replaces; {Path.GetFileName(sheetImagePath)} is {replacement.Width} × {replacement.Height}."));
        }

        var target = layer.ThemeResourceFile(resource);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(sheetImagePath, target, true);
    }

    private static string Resource(string name) => $"/sprites/{name}.png";

    // rows of pictures between transparent lines, then pictures between transparent columns of each row
    private static List<CharacterSheetFrame> FindFrames(SKBitmap sheet)
    {
        var frames = new List<CharacterSheetFrame>();
        var bands = Runs(sheet.Height, y => Enumerable.Range(0, sheet.Width).Any(x => sheet.GetPixel(x, y).Alpha > VisibleAlpha)).ToList();
        for (var row = 0; row < bands.Count; row++)
        {
            var (top, bottom) = bands[row];
            var boxes = Runs(sheet.Width, x => Enumerable.Range(top, bottom - top).Any(y => sheet.GetPixel(x, y).Alpha > VisibleAlpha)).ToList();

            // the slot reaches as far as the rows above and the pictures beside allow
            var (slotY, slotHeight) = Slot(top, bottom - top, row == 0 ? 0 : bands[row - 1].End, sheet.Height, alignEnd: true);
            for (var index = 0; index < boxes.Count; index++)
            {
                var (left, right) = boxes[index];
                var (slotX, slotWidth) = Slot(left, right - left, index == 0 ? 0 : boxes[index - 1].End, index + 1 < boxes.Count ? boxes[index + 1].Start : sheet.Width, alignEnd: false);
                frames.Add(new CharacterSheetFrame(row, index, left, top, right - left, bottom - top, slotX, slotY, slotWidth, slotHeight));
            }
        }

        return frames;
    }

    // a span of whole sprites around the picture (centred, or ending with it), between the free limits
    private static (int Start, int Length) Slot(int start, int length, int lowest, int highest, bool alignEnd)
    {
        var size = Math.Max(SpriteSide, (length + SpriteSide - 1) / SpriteSide * SpriteSide);
        if (size > highest - lowest)
        {
            return (start, length);
        }

        var wanted = alignEnd ? start + length - size : start - ((size - length) / 2);
        return (Math.Clamp(wanted, lowest, highest - size), size);
    }

    // A picture that fits is kept as it is. A larger one loses the enlargement of pixel art first (each of its pixels
    // drawn as a block of the same colour), then is made smaller until it fits, a source pixel to a sheet pixel.
    private static (SKBitmap Picture, double Scale) Fit(SKBitmap picture, int width, int height)
    {
        if (picture.Width <= width && picture.Height <= height)
        {
            return (picture.Copy(), 1);
        }

        var block = PixelBlock(picture);
        var scale = 1.0 / block;
        if (picture.Width / block > width || picture.Height / block > height)
        {
            scale = Math.Min((double)width / picture.Width, (double)height / picture.Height);
        }

        var target = new SKImageInfo(
            Math.Clamp((int)Math.Round(picture.Width * scale), 1, width),
            Math.Clamp((int)Math.Round(picture.Height * scale), 1, height),
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        return (picture.Resize(target, SKFilterQuality.None), scale);
    }

    // the side of the blocks enlarged pixel art is drawn with: what every run of one colour, across and down, is a
    // multiple of
    private static int PixelBlock(SKBitmap picture)
    {
        var block = 0;
        void Add(int run) => block = block == 0 ? run : Gcd(block, run);
        void Runs(int lines, int length, Func<int, int, SKColor> pixel)
        {
            for (var line = 0; line < lines && block != 1; line++)
            {
                var run = 1;
                for (var position = 1; position < length; position++)
                {
                    if (pixel(line, position) == pixel(line, position - 1))
                    {
                        run++;
                    }
                    else
                    {
                        Add(run);
                        run = 1;
                    }
                }

                Add(run);
            }
        }

        Runs(picture.Height, picture.Width, (y, x) => picture.GetPixel(x, y));
        Runs(picture.Width, picture.Height, (x, y) => picture.GetPixel(x, y));
        return Math.Max(1, block);
    }

    private static int Gcd(int first, int second) => second == 0 ? first : Gcd(second, first % second);

    // the part of the picture that is not transparent
    private static SKRectI? Ink(SKBitmap picture)
    {
        var rows = Runs(picture.Height, y => Enumerable.Range(0, picture.Width).Any(x => picture.GetPixel(x, y).Alpha > VisibleAlpha)).ToList();
        var columns = Runs(picture.Width, x => Enumerable.Range(0, picture.Height).Any(y => picture.GetPixel(x, y).Alpha > VisibleAlpha)).ToList();
        return rows.Count == 0 || columns.Count == 0 ? null : new SKRectI(columns[0].Start, rows[0].Start, columns[^1].End, rows[^1].End);
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
    private static (ThemeLayer Layer, string Path) DraftSheet(string themeDraftPath, string? baseUiPath, string resource)
    {
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var target = layer.ThemeResourceFile(resource);
        if (!File.Exists(target))
        {
            var source = layer.Resolve(resource) ?? throw new FileNotFoundException($"This UI has no resources{resource}.");
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
