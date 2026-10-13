using MiniThemeStudio.Core.Console;

namespace MiniThemeStudio.Infrastructure.Console;

public sealed class ConsoleDirectoryClientFactory : IConsoleDirectoryClientFactory
{
    public IConsoleDirectoryClient Create(ConsoleConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SshConsoleDirectoryClient(options);
    }
}
