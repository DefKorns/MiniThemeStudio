using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
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
    private readonly IDialogService _dialogs;
    private readonly IErrorHandler _errorHandler;

    public WallpapersViewModel(
        ThemeSession session,
        IThemeAssetManager assetManager,
        IFileDialogService fileDialogs,
        IDialogService dialogs,
        IErrorHandler errorHandler,
        ParallaxViewModel parallax,
        PixelBackgroundViewModel pixelBackground,
        EditingMode mode)
    {
        PixelBackground = pixelBackground;
        Mode = mode;
        Mode.PropertyChanged += (_, _) => Reload();
        _dialogs = dialogs;
        _session = session;
        Parallax = parallax;
        Parallax.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ParallaxViewModel.SelectedLayer)
                && Parallax.SelectedLayer is { } layer
                && Wallpapers.FirstOrDefault(wallpaper => wallpaper.Name == layer.Name) is { } drawn)
            {
                SelectedWallpaper = drawn;
            }
        };
        _assetManager = assetManager;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    // the scrolling layers of the background, shown in the same tab
    public ParallaxViewModel Parallax { get; }

    // simple editing lists wall, wall_near and wall_far; advanced editing every image of the folder
    public EditingMode Mode { get; }

    // the NES and Famicom background, drawn by a script with colours instead of wallpaper images
    public PixelBackgroundViewModel PixelBackground { get; }

    public bool HasWallpaperImages => !PixelBackground.HasPixelBackground;

    [ObservableProperty]
    private IReadOnlyList<WallpaperAsset> wallpapers = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWallpaperCommand), nameof(ExportWallpaperCommand), nameof(DeleteWallpaperCommand))]
    [NotifyPropertyChangedFor(nameof(DeleteWallpaperText))]
    private WallpaperAsset? selectedWallpaper;

    // bumped when the selected image is rewritten, so its preview is read again
    [ObservableProperty]
    private int previewRevision;

    private bool HasSelection => SelectedWallpaper is not null;

    // the theme's own image: a replacement goes back to the Base one, an added image no scene draws is removed
    private bool CanDeleteWallpaper => SelectedWallpaper is { ExistsInTheme: true } wallpaper && (wallpaper.OverridesBase || wallpaper.UsedBy.Count == 0);

    public string DeleteWallpaperText => SelectedWallpaper is { OverridesBase: true } ? "Use the Base image" : "Delete image";

    [RelayCommand(CanExecute = nameof(CanDeleteWallpaper))]
    private async Task DeleteWallpaperAsync()
    {
        if (_session.Draft is not { } draft || SelectedWallpaper is not { } wallpaper)
        {
            return;
        }

        var message = wallpaper.OverridesBase
            ? $"Remove this theme's {wallpaper.Name}.png? The image of the Base shows again."
            : $"Delete {wallpaper.Name}.png? No scene draws it, so it is removed from the theme.";
        if (!await _dialogs.ShowConfirmationAsync(DeleteWallpaperText, message, acceptButtonText: wallpaper.OverridesBase ? "Use the Base image" : "Delete", isDestructive: true))
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot remove {wallpaper.DisplayName}", () =>
        {
            _assetManager.DeleteThemeImage(draft.DraftPath, _session.BaseUiPath, $"layout/asset/sprite/wallpaper/{wallpaper.Name}.png");
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            PreviewRevision++;
            _session.Status = wallpaper.OverridesBase ? $"{wallpaper.DisplayName} uses the Base image again." : $"Deleted {wallpaper.DisplayName}.";
        }
    }

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

    partial void OnSelectedWallpaperChanged(WallpaperAsset? value)
    {
        if (value is not null && Parallax.Layers.FirstOrDefault(layer => layer.Name == value.Name) is { } layer)
        {
            Parallax.SelectedLayer = layer;
        }
    }

    private void Reload()
    {
        OnPropertyChanged(nameof(HasWallpaperImages));
        var selectedName = SelectedWallpaper?.Name;
        var report = _session.Report;
        Wallpapers = _session.Draft is { } draft
            ? _assetManager.GetWallpapers(draft.DraftPath, _session.BaseUiPath)
                .Select(wallpaper => wallpaper with
                {
                    // scenes use the id registered for the path, which may differ from the one in the image .json
                    UsedBy = report.UsageOf(report.TextureIdOf($"layout/asset/sprite/wallpaper/{wallpaper.Name}.png", wallpaper.TextureId)),
                })
                .Where(wallpaper => Mode.IsAdvanced || wallpaper.IsCore)
                .ToList()
            : [];
        SelectedWallpaper = Wallpapers.FirstOrDefault(wallpaper => wallpaper.Name == selectedName)
            ?? (Wallpapers.Count > 0 ? Wallpapers[0] : null);
    }
}
