using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class SpriteSheetImporter : ISpriteSheetImporter
{
    private const byte VisibleAlpha = 16;

    public uint GetPixel(string sheetPath, int x, int y)
    {
        using var bitmap = Decode(sheetPath);
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "The point is outside the sprite sheet.");
        }

        return (uint)bitmap.GetPixel(x, y);
    }

    public IReadOnlyList<uint> SuggestBackgroundColours(string sheetPath)
    {
        using var bitmap = Decode(sheetPath);
        var counts = new Dictionary<uint, int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var colour = bitmap.GetPixel(x, y);
                if (colour.Alpha > VisibleAlpha)
                {
                    counts[(uint)colour] = counts.GetValueOrDefault((uint)colour) + 1;
                }
            }
        }

        var suggestions = new List<uint>();
        var corner = bitmap.GetPixel(0, 0);
        if (corner.Alpha > VisibleAlpha)
        {
            suggestions.Add((uint)corner);
        }

        if (counts.Count > 0)
        {
            var mostFrequent = counts.MaxBy(entry => entry.Value).Key;
            if (!suggestions.Contains(mostFrequent))
            {
                suggestions.Add(mostFrequent);
            }
        }

        var cells = DetectFrames(sheetPath, new SheetKeying(suggestions));
        var cornerCounts = new Dictionary<uint, int>();
        foreach (var cell in cells.Where(cell => cell.Width > 2 && cell.Height > 2))
        {
            var corners = new[]
            {
                bitmap.GetPixel(cell.X, cell.Y),
                bitmap.GetPixel(cell.X + cell.Width - 1, cell.Y),
                bitmap.GetPixel(cell.X, cell.Y + cell.Height - 1),
                bitmap.GetPixel(cell.X + cell.Width - 1, cell.Y + cell.Height - 1),
            };
            foreach (var colour in corners.Select(corner => (uint)corner).Distinct())
            {
                cornerCounts[colour] = cornerCounts.GetValueOrDefault(colour) + 1;
            }
        }

        suggestions.AddRange(cornerCounts
            .Where(entry => entry.Value >= 3 && entry.Value >= cells.Count / 4 && !suggestions.Contains(entry.Key))
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Key));
        return suggestions;
    }

    public IReadOnlyList<AtlasRectangle> DetectFrames(string sheetPath, SheetKeying keying, int mergeGap = 1, int minimumPixels = 4, double maximumAreaShare = 0.5)
    {
        ArgumentNullException.ThrowIfNull(keying);
        using var bitmap = Decode(sheetPath);
        var width = bitmap.Width;
        var height = bitmap.Height;
        var foreground = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                foreground[(y * width) + x] = !IsTransparent(bitmap.GetPixel(x, y), keying);
            }
        }

        var boxes = new List<AtlasRectangle>();
        var visited = new bool[foreground.Length];
        var pending = new Stack<int>();
        for (var start = 0; start < foreground.Length; start++)
        {
            if (!foreground[start] || visited[start])
            {
                continue;
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            visited[start] = true;
            pending.Push(start);
            while (pending.Count > 0)
            {
                var index = pending.Pop();
                var x = index % width;
                var y = index / width;
                count++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var neighbour = (ny * width) + nx;
                        if (foreground[neighbour] && !visited[neighbour])
                        {
                            visited[neighbour] = true;
                            pending.Push(neighbour);
                        }
                    }
                }
            }

            if (count >= minimumPixels)
            {
                boxes.Add(new AtlasRectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
        }

        var maximumArea = maximumAreaShare * width * height;
        return OrderInRows(MergeNearby(boxes, Math.Max(0, mergeGap)))
            .Where(box => (double)box.Width * box.Height <= maximumArea)
            .ToList();
    }

    public IReadOnlyList<string> ExportFrames(
        string sheetPath,
        IReadOnlyList<AtlasRectangle> frames,
        SheetKeying keying,
        FrameAnchor anchor,
        AtlasSize? canvasSize,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(keying);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (frames.Count == 0)
        {
            throw new ArgumentException("Select at least one frame.", nameof(frames));
        }

        using var sheet = Decode(sheetPath);
        var canvasWidth = canvasSize?.Width ?? frames.Max(frame => frame.Width);
        var canvasHeight = canvasSize?.Height ?? frames.Max(frame => frame.Height);
        if (frames.Any(frame => frame.Width > canvasWidth || frame.Height > canvasHeight))
        {
            throw new InvalidDataException($"A selected frame is larger than the {canvasWidth} x {canvasHeight} target.");
        }

        Directory.CreateDirectory(outputDirectory);
        var paths = new List<string>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            if (frame.X < 0 || frame.Y < 0 || frame.X + frame.Width > sheet.Width || frame.Y + frame.Height > sheet.Height)
            {
                throw new InvalidDataException($"Frame {index + 1} is outside the sprite sheet.");
            }

            var offsetX = (canvasWidth - frame.Width) / 2;
            var offsetY = anchor == FrameAnchor.BottomCenter ? canvasHeight - frame.Height : (canvasHeight - frame.Height) / 2;
            using var output = new SKBitmap(new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            output.Erase(SKColors.Transparent);
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var color = sheet.GetPixel(frame.X + x, frame.Y + y);
                    output.SetPixel(offsetX + x, offsetY + y, IsTransparent(color, keying) ? SKColors.Transparent : color);
                }
            }

            var path = Path.Combine(outputDirectory, $"frame{index + 1:00}.png");
            using var image = SKImage.FromBitmap(output);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(path);
            data.SaveTo(stream);
            paths.Add(path);
        }

        return paths;
    }

    private static bool IsTransparent(SKColor color, SheetKeying keying)
    {
        if (color.Alpha <= VisibleAlpha)
        {
            return true;
        }

        foreach (var key in keying.TransparentColors)
        {
            var keyColor = (SKColor)key;
            if (Math.Abs(color.Red - keyColor.Red) <= keying.Tolerance
                && Math.Abs(color.Green - keyColor.Green) <= keying.Tolerance
                && Math.Abs(color.Blue - keyColor.Blue) <= keying.Tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static List<AtlasRectangle> MergeNearby(List<AtlasRectangle> boxes, int gap)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < boxes.Count && !merged; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    if (AreClose(boxes[i], boxes[j], gap))
                    {
                        boxes[i] = Union(boxes[i], boxes[j]);
                        boxes.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }
        }

        return boxes;
    }

    private static bool AreClose(AtlasRectangle first, AtlasRectangle second, int gap)
    {
        return first.X - gap <= second.X + second.Width
            && second.X - gap <= first.X + first.Width
            && first.Y - gap <= second.Y + second.Height
            && second.Y - gap <= first.Y + first.Height;
    }

    private static AtlasRectangle Union(AtlasRectangle first, AtlasRectangle second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Max(first.Y + first.Height, second.Y + second.Height);
        return new AtlasRectangle(left, top, right - left, bottom - top);
    }

    private static List<AtlasRectangle> OrderInRows(List<AtlasRectangle> boxes)
    {
        var rows = new List<List<AtlasRectangle>>();
        foreach (var box in boxes.OrderBy(box => box.Y))
        {
            var row = rows.FirstOrDefault(candidate =>
            {
                var top = candidate.Min(item => item.Y);
                var bottom = candidate.Max(item => item.Y + item.Height);
                return box.Y < bottom && box.Y + box.Height > top;
            });
            if (row is null)
            {
                rows.Add([box]);
            }
            else
            {
                row.Add(box);
            }
        }

        return rows.SelectMany(row => row.OrderBy(box => box.X)).ToList();
    }

    private static SKBitmap Decode(string sheetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetPath);
        return SKBitmap.Decode(sheetPath)
            ?? throw new InvalidDataException("The sprite sheet is not a valid PNG image.");
    }
}
