using System.Diagnostics;
using System.Formats.Tar;
using MiniThemeStudio.Core.Console;

namespace MiniThemeStudio.Infrastructure.Console;

internal sealed class SshConsoleDirectoryClient(
    ConsoleConnectionOptions options) : IConsoleDirectoryClient
{
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        CommandResult result = await RunCommandAsync("true", cancellationToken);
        EnsureSuccess(result, "SSH connection failed");
    }

    public async Task<bool> DirectoryExistsAsync(
        string remotePath,
        CancellationToken cancellationToken)
    {
        CommandResult result = await RunCommandAsync(
            $"test -d {QuoteShellArgument(remotePath)}",
            cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task DownloadDirectoryAsync(
        string remotePath,
        string localPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localPath);
        using Process process = StartProcess(
            $"tar -C {QuoteShellArgument(remotePath)} -cf - .");
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await TarFile.ExtractToDirectoryAsync(
            process.StandardOutput.BaseStream,
            localPath,
            false,
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"SSH download failed with exit code {process.ExitCode}: {error.Trim()}");
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private async Task<CommandResult> RunCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        using Process process = StartProcess(command);
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private Process StartProcess(string command)
    {
        var startInfo = new ProcessStartInfo("ssh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ConnectTimeout=5");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("StrictHostKeyChecking=accept-new");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("22");
        startInfo.ArgumentList.Add($"root@{options.Host}");
        startInfo.ArgumentList.Add(command);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the SSH client.");
    }

    private static string QuoteShellArgument(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static void EnsureSuccess(CommandResult result, string message)
    {
        if (result.ExitCode != 0)
        {
            throw new IOException(
                $"{message} with exit code {result.ExitCode}: {result.Error.Trim()}");
        }
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
