using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class SpriteSheetImporterTests : IDisposable
{
    private static readonly SKColor SheetBlue = new(0x00, 0xA2, 0xE8);
    private static readonly SKColor CellBlue = new(0x99, 0xD9, 0xEA);

    private readonly string _rootPath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _sheetPath;

    public SpriteSheetImporterTests()
    {
        _sheetPath = Path.Combine(_rootPath, "sheet.png");
        using var sheet = new SKBitmap(40, 30);
        sheet.Erase(SheetBlue);
        Fill(sheet, 1, 1, 18, 18, CellBlue);
        Fill(sheet, 21, 1, 18, 18, CellBlue);
        Fill(sheet, 5, 6, 4, 10, SKColors.Red);
        Fill(sheet, 9, 6, 2, 2, SKColors.Red);
        Fill(sheet, 25, 10, 6, 6, SKColors.Yellow);
        Fill(sheet, 4, 22, 2, 2, SKColors.White);
        Fill(sheet, 7, 22, 2, 2, SKColors.White);
        AtlasLayoutMatcherTests.SavePng(sheet, _sheetPath);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
    }

    [Fact]
    public void DetectsFramesInReadingOrderIgnoringKeyedBackgrounds()
    {
        var importer = new SpriteSheetImporter();
        var keying = new SheetKeying([importer.GetPixel(_sheetPath, 0, 0), importer.GetPixel(_sheetPath, 2, 2)]);

        var frames = importer.DetectFrames(_sheetPath, keying, mergeGap: 1);

        Assert.Equal(
            [new AtlasRectangle(5, 6, 6, 10), new AtlasRectangle(25, 10, 6, 6), new AtlasRectangle(4, 22, 5, 2)],
            frames);
    }

    [Fact]
    public void ExportsFramesOnCommonCanvasAlignedByTheFeet()
    {
        var importer = new SpriteSheetImporter();
        var keying = new SheetKeying([(uint)SheetBlue, (uint)CellBlue]);
        var frames = importer.DetectFrames(_sheetPath, keying).Take(2).ToList();

        var paths = importer.ExportFrames(_sheetPath, frames, keying, FrameAnchor.BottomCenter, null, Path.Combine(_rootPath, "out"));

        using var tall = SKBitmap.Decode(paths[0]);
        using var small = SKBitmap.Decode(paths[1]);
        Assert.Equal((6, 10), (tall.Width, tall.Height));
        Assert.Equal((6, 10), (small.Width, small.Height));
        Assert.Equal(0, tall.GetPixel(5, 9).Alpha);
        Assert.Equal(SKColors.Red, tall.GetPixel(0, 9));
        Assert.Equal(0, small.GetPixel(0, 3).Alpha);
        Assert.Equal(SKColors.Yellow, small.GetPixel(0, 9));
    }

    [Fact]
    public void SuggestsSheetAndCellBackgrounds()
    {
        var importer = new SpriteSheetImporter();

        var suggested = importer.SuggestBackgroundColours(_sheetPath);
        var frames = importer.DetectFrames(_sheetPath, new SheetKeying(suggested));

        Assert.Equal([(uint)SheetBlue, (uint)CellBlue], suggested);
        Assert.Equal(new AtlasRectangle(5, 6, 6, 10), frames[0]);
    }

    [Fact]
    public void IgnoresBlobsCoveringMostOfTheSheet()
    {
        var importer = new SpriteSheetImporter();

        Assert.Empty(importer.DetectFrames(_sheetPath, new SheetKeying([])));
    }

    [Fact]
    public void RejectsFramesLargerThanTheTargetSize()
    {
        var importer = new SpriteSheetImporter();
        var keying = new SheetKeying([(uint)SheetBlue, (uint)CellBlue]);
        var frames = importer.DetectFrames(_sheetPath, keying).Take(1).ToList();

        Assert.Throws<InvalidDataException>(() =>
            importer.ExportFrames(_sheetPath, frames, keying, FrameAnchor.BottomCenter, new AtlasSize(4, 4), _rootPath));
    }

    private static void Fill(SKBitmap bitmap, int x, int y, int width, int height, SKColor color)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                bitmap.SetPixel(column, row, color);
            }
        }
    }
}
