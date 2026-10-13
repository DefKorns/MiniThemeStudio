using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeDraftService(
    IThemeProjectCreator themeProjectCreator,
    Func<string?>? draftsRootPathProvider = null,
    string? backupsRootPath = null,
    int backupsToKeep = 5) : IThemeDraftService
{
    public static string DefaultBackupsRootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MiniThemeStudio",
        "Backups");

    private static readonly string DefaultDraftsRootPath = Path.Combine(
        Path.GetTempPath(),
        "MiniThemeStudio",
        "drafts");

    public ThemeDraft OpenExisting(string themePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);
        var source = new DirectoryInfo(themePath);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Theme directory not found: {source.FullName}");
        }

        var draftPath = CreateDraftDirectory();
        try
        {
            CopyDirectory(source.FullName, draftPath);
            return new ThemeDraft(draftPath, source.FullName, false);
        }
        catch
        {
            DeleteDirectory(draftPath);
            throw;
        }
    }

    public ThemeDraft CreateNew(NewThemeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureNewDestinationAvailable(request.DestinationPath);
        var draftPath = CreateDraftDirectory();

        try
        {
            themeProjectCreator.Create(request with { DestinationPath = draftPath });
            return new ThemeDraft(draftPath, request.DestinationPath, true);
        }
        catch
        {
            DeleteDirectory(draftPath);
            throw;
        }
    }

    public ThemeDraft Save(ThemeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!Directory.Exists(draft.DraftPath))
        {
            throw new DirectoryNotFoundException($"Draft directory not found: {draft.DraftPath}");
        }

        if (draft.RequiresEmptySavePath)
        {
            EnsureNewDestinationAvailable(draft.SavePath);
        }

        var destination = new DirectoryInfo(draft.SavePath);
        var parentPath = destination.Parent?.FullName
            ?? throw new InvalidOperationException("The theme destination must have a parent directory.");
        var stagingPath = Path.Combine(
            parentPath,
            $".{destination.Name}.mts-stage-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(
            parentPath,
            $".{destination.Name}.mts-backup-{Guid.NewGuid():N}");
        var destinationMoved = false;
        var lastBackupPath = destination.Exists ? KeepBackup(destination) : null;

        try
        {
            Directory.CreateDirectory(stagingPath);
            CopyDirectory(draft.DraftPath, stagingPath);

            if (destination.Exists)
            {
                Directory.Move(destination.FullName, backupPath);
                destinationMoved = true;
            }

            Directory.Move(stagingPath, destination.FullName);
            DeleteDirectory(backupPath);
            return draft with { RequiresEmptySavePath = false, LastBackupPath = lastBackupPath };
        }
        catch
        {
            DeleteDirectory(stagingPath);

            if (destinationMoved && !Directory.Exists(destination.FullName))
            {
                Directory.Move(backupPath, destination.FullName);
            }

            throw;
        }
    }

    public void Discard(ThemeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        DeleteDirectory(draft.DraftPath);
    }

    private string KeepBackup(DirectoryInfo destination)
    {
        var themeBackupsPath = Path.Combine(backupsRootPath ?? DefaultBackupsRootPath, destination.Name);
        var backupPath = Path.Combine(themeBackupsPath, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backupPath);
        CopyDirectory(destination.FullName, backupPath);

        foreach (var expired in new DirectoryInfo(themeBackupsPath).EnumerateDirectories()
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .Skip(Math.Max(1, backupsToKeep)))
        {
            expired.Delete(true);
        }

        return backupPath;
    }

    private string CreateDraftDirectory()
    {
        var configuredRootPath = draftsRootPathProvider?.Invoke();
        var rootPath = string.IsNullOrWhiteSpace(configuredRootPath)
            ? DefaultDraftsRootPath
            : configuredRootPath;
        var draftPath = Path.Combine(rootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(draftPath);
        return draftPath;
    }

    private static void EnsureNewDestinationAvailable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var parentPath = Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(
                $"Destination parent directory not found: {parentPath}");
        }

        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException($"The destination already exists: {path}");
        }
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        var source = new DirectoryInfo(sourcePath);

        foreach (var directory in source.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source.FullName, directory.FullName);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
        }

        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source.FullName, file.FullName);
            var destinationFilePath = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            file.CopyTo(destinationFilePath);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
