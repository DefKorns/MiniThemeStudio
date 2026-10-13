using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class TextureAtlasEditor : ITextureAtlasEditor
{
    public SpriteReplacementResult ReplaceSprite(SpriteReplacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var atlas = DecodeBitmap(request.AtlasImagePath, "atlas");
        using var replacement = DecodeBitmap(request.ReplacementImagePath, "replacement");
        var frame = request.Sprite.Frame;

        if (replacement.Width != frame.Width || replacement.Height != frame.Height)
        {
            throw new InvalidDataException(
                $"Replacement must be {frame.Width} x {frame.Height} pixels, "
                + $"but is {replacement.Width} x {replacement.Height} pixels.");
        }

        if (frame.X < 0
            || frame.Y < 0
            || frame.X + frame.Width > atlas.Width
            || frame.Y + frame.Height > atlas.Height)
        {
            throw new InvalidDataException("The sprite frame is outside the atlas image.");
        }

        using (var canvas = new SKCanvas(atlas))
        using (var paint = new SKPaint
        {
            BlendMode = SKBlendMode.Src,
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
        })
        {
            canvas.DrawBitmap(replacement, frame.X, frame.Y, paint);
            canvas.Flush();
        }

        var temporaryPath = request.AtlasImagePath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            SavePng(atlas, temporaryPath);
            File.Move(temporaryPath, request.AtlasImagePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new SpriteReplacementResult();
    }

    private static SKBitmap DecodeBitmap(string path, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The {description} PNG was not found.", path);
        }

        return SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"The {description} file is not a valid PNG image.");
    }

    private static void SavePng(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The atlas could not be encoded as PNG.");
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
