namespace MiniThemeStudio.Core.Console;

public interface IConsoleDirectoryClientFactory
{
    IConsoleDirectoryClient Create(ConsoleConnectionOptions options);
}
