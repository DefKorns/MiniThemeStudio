namespace MiniThemeStudio.Core.Themes;

/// <summary>A scene component that draws with a palette colour through its colorName.</summary>
public sealed record PaletteColorUsage(string ColorName, string ScenePath, string NodePath, string? TextKey);
