namespace MiniThemeStudio.App.Models;

// AdvancedEditing opens the theme with every tab, not only the ones a simple theme needs
public sealed record NewThemeDetails(string Name, string Author, string BaseUiPath, string? AtlasImagePath = null, bool AdvancedEditing = false);
