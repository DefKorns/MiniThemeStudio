using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniThemeStudio.App.Services;

// e.g. new FileType("PNG image", "*.png")
public sealed record FileType(string Name, params string[] Patterns);

// System file and folder pickers; every method returns local paths, or null / empty when the user cancels.
public interface IFileDialogService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> OpenFileAsync(string title, params FileType[] fileTypes);

    Task<IReadOnlyList<string>> OpenFilesAsync(string title, params FileType[] fileTypes);

    Task<string?> SaveFileAsync(string title, string suggestedFileName, string defaultExtension, params FileType[] fileTypes);
}
