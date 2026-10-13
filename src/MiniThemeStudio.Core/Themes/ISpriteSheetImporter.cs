namespace MiniThemeStudio.Core.Themes;

public enum FrameAnchor
{
    BottomCenter,
    Center,
}

public sealed record SheetKeying(IReadOnlyList<uint> TransparentColors, int Tolerance = 8);

public interface ISpriteSheetImporter
{
    uint GetPixel(string sheetPath, int x, int y);

    IReadOnlyList<uint> SuggestBackgroundColours(string sheetPath);

    IReadOnlyList<AtlasRectangle> DetectFrames(string sheetPath, SheetKeying keying, int mergeGap = 1, int minimumPixels = 4, double maximumAreaShare = 0.5);

    IReadOnlyList<string> ExportFrames(
        string sheetPath,
        IReadOnlyList<AtlasRectangle> frames,
        SheetKeying keying,
        FrameAnchor anchor,
        AtlasSize? canvasSize,
        string outputDirectory);
}
