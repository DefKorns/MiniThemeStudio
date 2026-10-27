using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Controls;

// Source is drawn at Placement inside an image of LogicalSize (atlas sprites can be trimmed); Pivot is in LogicalSize pixels
public sealed record SceneImage(Bitmap Bitmap, Rect Source, Size LogicalSize, Rect Placement, Point? Pivot);

public sealed class SceneImageSource : IDisposable
{
    private readonly IReadOnlyDictionary<string, string> _imagePathByTextureId;
    private readonly Dictionary<string, TextureAtlas> _atlasByTextureId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextureAtlas> _atlasByImagePath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public SceneImageSource(IReadOnlyDictionary<string, string> imagePathByTextureId, IEnumerable<TextureAtlas> atlases)
    {
        _imagePathByTextureId = imagePathByTextureId;
        foreach (var atlas in atlases)
        {
            _atlasByTextureId.TryAdd(atlas.TextureId, atlas);
            _atlasByImagePath.TryAdd(Path.GetFullPath(atlas.ImagePath), atlas);
        }
    }

    public static SceneImageSource Empty { get; } = new(new Dictionary<string, string>(), []);

    public SceneImage? Resolve(SceneVisual visual)
    {
        switch (visual.Kind)
        {
            case SceneVisualKind.Texture when visual.TextureId is { } textureId
                && _imagePathByTextureId.TryGetValue(textureId, out var imagePath)
                && Load(imagePath) is { } bitmap:
                var size = new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                return new SceneImage(bitmap, new Rect(size), size, new Rect(size), null);
            case SceneVisualKind.Sprite when visual.SpriteId is { } spriteId && spriteId.IndexOf('.') is var dot and > 0:
                return ResolveSprite(spriteId[..dot], spriteId[(dot + 1)..]);
            default:
                return null;
        }
    }

    // spriteId is "<atlas texture id>.<sprite name>"; the id may be the atlas json id or the one registered in dependencies.json
    private SceneImage? ResolveSprite(string textureId, string spriteName)
    {
        var atlas = _atlasByTextureId.GetValueOrDefault(textureId)
            ?? (_imagePathByTextureId.TryGetValue(textureId, out var imagePath) ? _atlasByImagePath.GetValueOrDefault(Path.GetFullPath(imagePath)) : null);
        var sprite = atlas?.Sprites.FirstOrDefault(candidate => candidate.Name == spriteName);
        if (atlas is null || sprite is null || Load(atlas.ImagePath) is not { } bitmap)
        {
            return null;
        }

        var frame = sprite.Frame;
        return new SceneImage(
            bitmap,
            new Rect(frame.X, frame.Y, frame.Width, frame.Height),
            new Size(sprite.SourceSize.Width, sprite.SourceSize.Height),
            new Rect(sprite.SourceOffset.X, sprite.SourceOffset.Y, frame.Width, frame.Height),
            new Point(sprite.Pivot.X, sprite.Pivot.Y));
    }

    private Bitmap? Load(string path)
    {
        if (_bitmaps.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            bitmap = File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
        }

        _bitmaps[path] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var bitmap in _bitmaps.Values)
        {
            bitmap?.Dispose();
        }

        _bitmaps.Clear();
    }
}
