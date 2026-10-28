using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class SavedDraftStore(string? storeRootPath = null, Func<string?>? workingRootPathProvider = null) : ISavedDraftStore
{
    public const string ManifestFileName = ".mts-draft.json";

    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultStoreRootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MiniThemeStudio",
        "Drafts");

    private string StoreRootPath => storeRootPath ?? DefaultStoreRootPath;

    public IReadOnlyList<SavedDraft> List()
    {
        if (!Directory.Exists(StoreRootPath))
        {
            return [];
        }

        var drafts = new List<SavedDraft>();
        foreach (string? directory in Directory.EnumerateDirectories(StoreRootPath).Where(path => !Path.GetFileName(path).StartsWith('.')))
        {
            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                Manifest? manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), ManifestOptions);
                if (manifest is not null && manifest.Id == Path.GetFileName(directory))
                {
                    drafts.Add(new SavedDraft(manifest.Id, manifest.Name, manifest.SavePath, manifest.RequiresEmptySavePath, manifest.BaseUiPath, manifest.SavedAt, directory));
                }
            }
            catch (JsonException)
            {
            }
        }

        return drafts.OrderByDescending(draft => draft.SavedAt).ToList();
    }

    public SavedDraft Save(ThemeDraft draft, string name, string? baseUiPath)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Directory.Exists(draft.DraftPath))
        {
            throw new DirectoryNotFoundException($"Draft directory not found: {draft.DraftPath}");
        }

        string id = draft.SavedDraftId ?? Guid.NewGuid().ToString("N");
        string targetPath = SnapshotPath(id);
        string stagingPath = Path.Combine(StoreRootPath, $".{id}.stage-{Guid.NewGuid():N}");
        string backupPath = Path.Combine(StoreRootPath, $".{id}.backup-{Guid.NewGuid():N}");
        bool targetMoved = false;
        var manifest = new Manifest(id, name.Trim(), draft.SavePath, draft.RequiresEmptySavePath, baseUiPath, DateTimeOffset.Now);

        try
        {
            CopyDirectory(draft.DraftPath, stagingPath);
            File.WriteAllText(Path.Combine(stagingPath, ManifestFileName), JsonSerializer.Serialize(manifest, ManifestOptions));
            if (Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, backupPath);
                targetMoved = true;
            }

            Directory.Move(stagingPath, targetPath);
            if (targetMoved)
            {
                Directory.Delete(backupPath, true);
            }
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            if (targetMoved && !Directory.Exists(targetPath))
            {
                Directory.Move(backupPath, targetPath);
            }

            throw;
        }

        return new SavedDraft(id, manifest.Name, manifest.SavePath, manifest.RequiresEmptySavePath, baseUiPath, manifest.SavedAt, targetPath);
    }

    public ThemeDraft Restore(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        string snapshotPath = SnapshotPath(savedDraft.Id);
        if (!File.Exists(Path.Combine(snapshotPath, ManifestFileName)))
        {
            throw new DirectoryNotFoundException($"Saved draft not found: {savedDraft.Name}");
        }

        string? workingRootPath = workingRootPathProvider?.Invoke();
        string workingPath = Path.Combine(
            string.IsNullOrWhiteSpace(workingRootPath) ? Path.Combine(Path.GetTempPath(), "MiniThemeStudio", "drafts") : workingRootPath,
            Guid.NewGuid().ToString("N"));
        CopyDirectory(snapshotPath, workingPath);
        File.Delete(Path.Combine(workingPath, ManifestFileName));
        return new ThemeDraft(workingPath, savedDraft.SavePath, savedDraft.RequiresEmptySavePath, null, savedDraft.Id);
    }

    public void Delete(string id)
    {
        string snapshotPath = SnapshotPath(id);
        if (Directory.Exists(snapshotPath))
        {
            Directory.Delete(snapshotPath, true);
        }
    }

    private string SnapshotPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException($"Invalid saved draft id: {id}", nameof(id));
        }

        return Path.Combine(StoreRootPath, id);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string targetPath = Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, filePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(filePath, targetPath);
        }
    }

    private sealed record Manifest(string Id, string Name, string SavePath, bool RequiresEmptySavePath, string? BaseUiPath, DateTimeOffset SavedAt);
}
