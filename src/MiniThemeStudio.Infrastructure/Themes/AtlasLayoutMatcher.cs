using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class AtlasLayoutMatcher(ITextureAtlasReader? atlasReader = null) : IAtlasLayoutMatcher
{
    private const byte VisibleAlpha = 16;

    private readonly ITextureAtlasReader _atlasReader = atlasReader ?? new TextureAtlasReader();

    public IReadOnlyList<AtlasLayoutMatch> Rank(string imagePath, IEnumerable<InstalledBaseInfo> bases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(bases);

        using var bitmap = SKBitmap.Decode(imagePath)
            ?? throw new InvalidDataException("The atlas source is not a valid PNG image.");
        var visible = ReadVisibility(bitmap);
        var visibleCount = visible.Count(isVisible => isVisible);

        var matches = new List<AtlasLayoutMatch>();
        foreach (var installedBase in bases)
        {
            var atlas = _atlasReader.Read(installedBase.LocalPath);
            if (atlas is null || atlas.Size.Width != bitmap.Width || atlas.Size.Height != bitmap.Height)
            {
                continue;
            }

            var covered = new bool[visible.Length];
            var framesWithContent = 0;
            foreach (var sprite in atlas.Sprites)
            {
                var hasContent = false;
                var frame = sprite.Frame;
                for (var y = Math.Max(0, frame.Y); y < Math.Min(bitmap.Height, frame.Y + frame.Height); y++)
                {
                    for (var x = Math.Max(0, frame.X); x < Math.Min(bitmap.Width, frame.X + frame.Width); x++)
                    {
                        var index = (y * bitmap.Width) + x;
                        covered[index] = true;
                        hasContent |= visible[index];
                    }
                }

                if (hasContent)
                {
                    framesWithContent++;
                }
            }

            var visibleInside = 0;
            for (var index = 0; index < visible.Length; index++)
            {
                if (visible[index] && covered[index])
                {
                    visibleInside++;
                }
            }

            var coverage = visibleCount == 0 ? 0 : 100.0 * visibleInside / visibleCount;
            matches.Add(new AtlasLayoutMatch(installedBase, atlas.Name, coverage, framesWithContent, atlas.Sprites.Count));
        }

        return matches
            .OrderByDescending(match => Math.Round(match.CoveragePercent, 1))
            .ThenByDescending(match => match.FramesWithContent)
            .ThenByDescending(match => Version.TryParse(match.Base.Definition.Version, out var version) ? version : new Version())
            .ToList();
    }

    private static bool[] ReadVisibility(SKBitmap bitmap)
    {
        var visible = new bool[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                visible[(y * bitmap.Width) + x] = bitmap.GetPixel(x, y).Alpha > VisibleAlpha;
            }
        }

        return visible;
    }
}
