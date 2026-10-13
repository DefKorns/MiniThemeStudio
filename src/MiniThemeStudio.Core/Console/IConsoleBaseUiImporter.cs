using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Core.Console;

public interface IConsoleBaseUiImporter
{
    Task<BaseUiImportResult> ImportAvailableAsync(
        string baseUiRootPath,
        ConsoleConnectionOptions connection,
        CancellationToken cancellationToken = default);

    Task<BaseUiImportResult> ImportAsync(
        BaseUiDefinition definition,
        string baseUiRootPath,
        ConsoleConnectionOptions connection,
        CancellationToken cancellationToken = default);
}
