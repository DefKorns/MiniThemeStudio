using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class SavedDraftStoreTests : IDisposable
{
    private readonly string _rootPath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _storePath;
    private readonly string _workingRootPath;
    private readonly ThemeDraft _draft;

    public SavedDraftStoreTests()
    {
        _storePath = Path.Combine(_rootPath, "store");
        _workingRootPath = Path.Combine(_rootPath, "working");
        string draftPath = Path.Combine(_rootPath, "draft");
        Directory.CreateDirectory(Path.Combine(draftPath, "resources"));
        File.WriteAllText(Path.Combine(draftPath, "resources", "asset.txt"), "v1");
        _draft = new ThemeDraft(draftPath, Path.Combine(_rootPath, "themes", "samus"), true);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
    }

    [Fact]
    public void SavesListsAndRestoresAnIndependentWorkingCopy()
    {
        var store = new SavedDraftStore(_storePath, () => _workingRootPath);

        SavedDraft saved = store.Save(_draft, "Samus", "C:/bases/snes");
        SavedDraft listed = Assert.Single(store.List());
        ThemeDraft restored = store.Restore(listed);
        File.WriteAllText(Path.Combine(restored.DraftPath, "resources", "asset.txt"), "edited");

        Assert.Equal(saved with { SavedAt = listed.SavedAt }, listed);
        Assert.Equal(("Samus", "C:/bases/snes", true), (listed.Name, listed.BaseUiPath, listed.RequiresEmptySavePath));
        Assert.Equal(_draft.SavePath, restored.SavePath);
        Assert.Equal(saved.Id, restored.SavedDraftId);
        Assert.StartsWith(_workingRootPath, restored.DraftPath);
        Assert.False(File.Exists(Path.Combine(restored.DraftPath, SavedDraftStore.ManifestFileName)));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(saved.SnapshotPath, "resources", "asset.txt")));
    }

    [Fact]
    public void SavingAgainReplacesTheSameSnapshot()
    {
        var store = new SavedDraftStore(_storePath, () => _workingRootPath);
        SavedDraft first = store.Save(_draft, "Samus", null);
        File.WriteAllText(Path.Combine(_draft.DraftPath, "resources", "asset.txt"), "v2");

        SavedDraft second = store.Save(_draft with { SavedDraftId = first.Id }, "Samus v2", null);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Samus v2", Assert.Single(store.List()).Name);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(second.SnapshotPath, "resources", "asset.txt")));
        Assert.Equal([first.Id], Directory.GetDirectories(_storePath).Select(Path.GetFileName));
    }

    [Fact]
    public void DeletesSnapshotsAndRejectsUnsafeIds()
    {
        var store = new SavedDraftStore(_storePath, () => _workingRootPath);
        SavedDraft saved = store.Save(_draft, "Samus", null);

        store.Delete(saved.Id);

        Assert.Empty(store.List());
        Assert.Throws<ArgumentException>(() => store.Delete(".."));
    }
}
