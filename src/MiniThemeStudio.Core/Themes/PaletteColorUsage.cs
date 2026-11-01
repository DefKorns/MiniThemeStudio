namespace MiniThemeStudio.Core.Themes;

/// <summary>
/// A scene component that draws with a palette colour through its colorName. TextKey is what the scene holds for a
/// label and Text what the Base's strings make of it, the words the console shows.
/// </summary>
public sealed record PaletteColorUsage(
    string ColorName,
    string ScenePath,
    string NodePath,
    string? TextKey,
    string? Text = null);
