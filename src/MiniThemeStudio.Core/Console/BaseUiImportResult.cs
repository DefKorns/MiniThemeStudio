using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Core.Console;

public sealed record BaseUiImportResult(
    BaseUiDefinition Definition,
    string LocalPath,
    string RemotePath);
