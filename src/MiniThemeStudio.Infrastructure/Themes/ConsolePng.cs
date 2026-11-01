using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

/// <summary>
/// Writes the PNGs a theme ships. The console engine reads every PNG as 8-bit RGBA through libpng, which applies the
/// gamma and colour profile chunks a file carries, so a theme PNG is always written as plain 8-bit RGBA, not
/// interlaced, with none of those chunks, whatever the file it came from was.
/// </summary>
internal static class ConsolePng
{
    private static readonly SKImageInfo Plain = new(0, 0, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    public static SKData Encode(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var info = new SKImageInfo(bitmap.Width, bitmap.Height, Plain.ColorType, Plain.AlphaType);
        using var plain = new SKBitmap(info);
        using (SKImage image = SKImage.FromBitmap(bitmap))
        {
            if (!image.ReadPixels(info, plain.GetPixels(), plain.RowBytes, 0, 0))
            {
                throw new InvalidDataException("The image could not be converted to RGBA.");
            }
        }

        // no colour space on the pixels, so the encoder has no profile or gamma chunk to write
        using var pixmap = new SKPixmap(info, plain.GetPixels(), plain.RowBytes);
        return pixmap.Encode(SKPngEncoderOptions.Default)
            ?? throw new InvalidDataException("The image could not be encoded as PNG.");
    }

    public static void Save(SKBitmap bitmap, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using SKData data = Encode(bitmap);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        using (FileStream stream = File.Create(temporary))
        {
            data.SaveTo(stream);
        }

        File.Move(temporary, path, true);
    }
}
