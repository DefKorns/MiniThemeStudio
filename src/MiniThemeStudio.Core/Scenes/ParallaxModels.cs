namespace MiniThemeStudio.Core.Scenes;

// One image of a scrolling background. Its texture slides LoopsPerCycle times its own width every cycle of
// the background animation, so wider or faster layers seem nearer.
public sealed record ParallaxLayer(
    string Name,
    string NodePath,
    double Y,
    int ZIndex,
    double TileWidth,
    double LoopsPerCycle,
    double CycleSeconds)
{
    public double PixelsPerSecond => CycleSeconds > 0 ? LoopsPerCycle * TileWidth / CycleSeconds : 0;
}

// A background whose layers scroll: the scene animation that slides their textures, and the node that plays it
// (the new layers become its children). AnimatorLabel names that node by the names leading to it, e.g. demo_bg/RootPane.
public sealed record ParallaxBackground(
    string SceneResourcePath,
    string AnimationResourcePath,
    string AnimatorPath,
    string AnimatorName,
    string AnimatorLabel,
    double CycleSeconds,
    IReadOnlyList<ParallaxLayer> Layers)
{
    public string DisplayName => $"{AnimatorLabel} ({System.IO.Path.GetFileNameWithoutExtension(AnimationResourcePath)})";
}

// Scale draws each image pixel as Scale x Scale screen pixels, as the console's own layers do (3).
public sealed record NewParallaxLayer(string Name, string SourceImagePath, double Y, int ZIndex, int Scale, int LoopsPerCycle);

public interface IParallaxEditor
{
    // the scrolling backgrounds of the theme; none when its Base draws the background from a script
    IReadOnlyList<ParallaxBackground> GetBackgrounds(string themePath, string? baseUiPath);

    // returns the new layer's node path
    string AddLayer(string themeDraftPath, string? baseUiPath, ParallaxBackground background, NewParallaxLayer layer);

    // a whole number of loops per cycle keeps the cycle seamless; a negative one scrolls the other way
    void SetSpeed(string themeDraftPath, string? baseUiPath, ParallaxBackground background, string layerName, int loopsPerCycle);

    // removes the layer's node and its track in the animation
    void RemoveLayer(string themeDraftPath, string? baseUiPath, ParallaxBackground background, string layerName);
}
