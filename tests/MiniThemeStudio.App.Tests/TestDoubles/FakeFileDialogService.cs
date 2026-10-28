using MiniThemeStudio.App.Services;

namespace MiniThemeStudio.App.Tests.TestDoubles;

// returns the queued paths in order; an empty queue means the user cancelled
public sealed class FakeFileDialogService : IFileDialogService
{
    public Queue<string> Paths { get; } = new();

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Next());

    public Task<string?> OpenFileAsync(string title, params FileType[] fileTypes) => Task.FromResult(Next());

    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, params FileType[] fileTypes) =>
        Task.FromResult<IReadOnlyList<string>>(Next() is { } path ? [path] : []);

    public Task<string?> SaveFileAsync(string title, string suggestedFileName, string defaultExtension, params FileType[] fileTypes) =>
        Task.FromResult(Next());

    private string? Next() => Paths.Count > 0 ? Paths.Dequeue() : null;
}
