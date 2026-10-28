using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The Wallpapers tab: the background layers (wall, wall_near, wall_far, poles...).
public sealed partial class WallpapersViewModel : ViewModelBase
{
    private static readonly FileType Png = new("PNG image", "*.png");

    private readonly ThemeSession _session;
    private readonly IThemeAssetManager _assetManager;
    private readonly IFileDialogService _fileDialogs;
    private readonly IErrorHandler _errorHandler;

    public WallpapersViewModel(ThemeSession session, IThemeAssetManager assetManager, IFileDialogService fileDialogs, IErrorHandler errorHandler)
    {
        _session = session;
        _assetManager = assetManager;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    [ObservableProperty]
    private IReadOnlyList<WallpaperAsset> wallpapers = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWallpaperCommand), nameof(ExportWallpaperCommand))]
    private WallpaperAsset? selectedWallpaper;

    // bumped when the selected image is rewritten, so its preview is read again
    [ObservableProperty]
    private int previewRevision;

    private bool HasSelection => SelectedWallpaper is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReplaceWallpaperAsync()
    {
        if (_session.Draft is not { } draft
            || SelectedWallpaper is not { } wallpaper
            || await _fileDialogs.OpenFileAsync($"Select replacement PNG for {wallpaper.DisplayName}", Png) is not { } replacementPath)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot replace {wallpaper.DisplayName}", () =>
        {
            var target = Path.Combine(draft.DraftPath, "resources", "layout", "asset", "sprite", "wallpaper", $"{wallpaper.Name}.png");
            _assetManager.ReplaceWallpaper(target, replacementPath, wallpaper.JsonPath);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            PreviewRevision++;
            _session.Status = $"Replaced wallpaper {wallpaper.DisplayName}.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ExportWallpaperAsync()
    {
        if (SelectedWallpaper is not { } wallpaper
            || await _fileDialogs.SaveFileAsync("Export wallpaper", $"{wallpaper.Name}.png", "png", Png) is not { } savePath)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot export {wallpaper.DisplayName}", () =>
        {
            _assetManager.ExportWallpaper(wallpaper, savePath);
            return Task.CompletedTask;
        }))
        {
            _session.Status = $"Exported wallpaper {wallpaper.DisplayName}.";
        }
    }

    private void Reload()
    {
        var selectedName = SelectedWallpaper?.Name;
        var report = _session.Report;
        Wallpapers = _session.Draft is { } draft
            ? _assetManager.GetWallpapers(draft.DraftPath, _session.BaseUiPath)
                .Select(wallpaper => wallpaper with
                {
                    // scenes use the id registered for the path, which may differ from the one in the image .json
                    UsedBy = report.UsageOf(report.TextureIdOf($"layout/asset/sprite/wallpaper/{wallpaper.Name}.png", wallpaper.TextureId)),
                })
                .ToList()
            : [];
        SelectedWallpaper = Wallpapers.FirstOrDefault(wallpaper => wallpaper.Name == selectedName)
            ?? (Wallpapers.Count > 0 ? Wallpapers[0] : null);
    }
}
