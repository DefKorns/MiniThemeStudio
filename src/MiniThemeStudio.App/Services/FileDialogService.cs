using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace MiniThemeStudio.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await ActiveWindow.Get().StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> OpenFileAsync(string title, params FileType[] fileTypes)
    {
        var files = await OpenAsync(title, false, fileTypes);
        return files.Count > 0 ? files[0] : null;
    }

    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, params FileType[] fileTypes)
    {
        return OpenAsync(title, true, fileTypes);
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedFileName, string defaultExtension, params FileType[] fileTypes)
    {
        var file = await ActiveWindow.Get().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension,
            FileTypeChoices = ToPickerTypes(fileTypes),
        });

        return file?.TryGetLocalPath();
    }

    private static async Task<IReadOnlyList<string>> OpenAsync(string title, bool allowMultiple, FileType[] fileTypes)
    {
        var files = await ActiveWindow.Get().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = ToPickerTypes(fileTypes),
        });

        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }

    private static List<FilePickerFileType>? ToPickerTypes(FileType[] fileTypes)
    {
        return fileTypes.Length == 0
            ? null
            : fileTypes.Select(fileType => new FilePickerFileType(fileType.Name) { Patterns = fileType.Patterns }).ToList();
    }
}
