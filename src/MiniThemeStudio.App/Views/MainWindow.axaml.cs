using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Lucide.Avalonia;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Console;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Settings;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly DialogService _dialogService;
    private readonly ErrorHandlingService _errorHandlingService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IBaseFirmwareManager _baseFirmwareManager;
    private readonly IThemeAssetManager _themeAssetManager;
    private readonly IThemeValidator _themeValidator;

    private string _atlasImagePath = "";
    private bool _isCloseConfirmed;
    private bool _isCloseConfirmationOpen;
    private Bitmap? _atlasBitmap;
    private CroppedBitmap? _spritePreview;
    private Bitmap? _wallpaperBitmap;
    private Bitmap? _extraAssetBitmap;
    private readonly Avalonia.Threading.DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch _animationClock = System.Diagnostics.Stopwatch.StartNew();

    public MainWindow()
        : this(
            new JsonAppSettingsStore(),
            new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), new ConsoleDirectoryClientFactory()))
    {
    }

    public MainWindow(
        IAppSettingsStore settingsStore,
        IBaseFirmwareManager baseFirmwareManager,
        IThemeAssetManager? themeAssetManager = null,
        IThemeValidator? themeValidator = null)
    {
        _settingsStore = settingsStore;
        _baseFirmwareManager = baseFirmwareManager;
        _themeAssetManager = themeAssetManager ?? new ThemeAssetManager();
        _themeValidator = themeValidator ?? new ThemeValidator();

        _dialogService = new DialogService();
        _errorHandlingService = new ErrorHandlingService(_dialogService);
        InitializeComponent();
        Opened += MainWindow_Opened;
        _animationTimer.Tick += AnimationTimer_Tick;
        _animationTimer.Start();

        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += Application_ActualThemeVariantChanged;
            UpdateThemeModeButton(application.ActualThemeVariant);
        }
    }

    private async void ShowSettings_Click(object? sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync(false);
    }

    private async void ManageBases_Click(object? sender, RoutedEventArgs e)
    {
        await ShowBaseManagementAsync();
    }

    private Task ShowBaseManagementAsync()
    {
        return new BaseManagementDialog(_baseFirmwareManager, _settingsStore.Current).ShowDialog(this);
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        Opened -= MainWindow_Opened;
        if (!_settingsStore.Current.InitialSetupCompleted && !await ShowSettingsAsync(true))
        {
            Close();
            return;
        }

        if (_baseFirmwareManager.FindCompatibleBaseUiPath() is null)
        {
            await ShowBaseManagementAsync();
        }
    }

    private async Task<bool> ShowSettingsAsync(bool isInitialSetup)
    {
        var settings = await new SettingsDialog(
            _settingsStore.Current,
            isInitialSetup).ShowDialog<AppSettings?>(this);
        if (settings is null)
        {
            return false;
        }

        try
        {
            _settingsStore.Save(settings);
            if (Application.Current is { } application)
            {
                application.RequestedThemeVariant = settings.Theme switch
                {
                    ApplicationThemePreference.Light => ThemeVariant.Light,
                    ApplicationThemePreference.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                };
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            await _errorHandlingService.ShowErrorAsync(
                this,
                "Cannot save settings",
                exception.Message);
            return false;
        }
    }

    private async void OpenTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardDraftAsync())
        {
            return;
        }

        var path = await PickFolderAsync("Edit existing theme");
        if (path is not null && DataContext is MainWindowViewModel viewModel)
        {
            var baseUiPath = _baseFirmwareManager.FindCompatibleBaseUiPath(path);
            if (baseUiPath is null)
            {
                baseUiPath = await AskForBaseAsync(
                    path,
                    "The console of this theme could not be detected. Choose the Base it was made for.");
                if (baseUiPath is null && !HasAtlasDefinition(path))
                {
                    return;
                }
            }

            if (viewModel.OpenTheme(path, baseUiPath))
            {
                LoadAtlasImage(viewModel.AtlasImagePath);
                ShowSpritePreview(viewModel.SelectedSprite);
            }
            else
            {
                await _errorHandlingService.ShowErrorAsync(
                    this,
                    "Cannot open theme",
                    viewModel.Status);
            }
        }
    }

    private async void ReplaceSprite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select replacement PNG",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PNG image")
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"],
                },
            ],
        });

        var replacementPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (replacementPath is null)
        {
            return;
        }

        DisposePreview();
        var replaced = viewModel.ReplaceSelectedSprite(replacementPath);
        LoadAtlasImage(viewModel.AtlasImagePath);
        ShowSpritePreview(viewModel.SelectedSprite);

        if (!replaced)
        {
            await _errorHandlingService.ShowErrorAsync(
                this,
                "Cannot replace sprite",
                viewModel.Status);
        }
    }

    private async void ExportSprite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedSprite is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export sprite to PNG",
            DefaultExtension = "png",
            SuggestedFileName = $"{viewModel.SelectedSprite.Name}.png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] }
            ]
        });

        var savePath = file?.TryGetLocalPath();
        if (savePath != null)
        {
            viewModel.ExportSelectedSprite(savePath);
        }
    }

    private async void SaveTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && !viewModel.SaveTheme())
        {
            await _errorHandlingService.ShowErrorAsync(
                this,
                "Cannot save theme",
                viewModel.Status);
        }
    }

    private async void SendToOmThemes_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CurrentDraftPath: not null } viewModel)
        {
            return;
        }

        var omThemesRootPath = _settingsStore.Current.OmThemesRootPath;
        if (!Directory.Exists(Path.Combine(omThemesRootPath, "new-packaging")))
        {
            omThemesRootPath = await PickFolderAsync("Select your om_themes folder (the one containing new-packaging)") ?? "";
            if (omThemesRootPath.Length == 0)
            {
                return;
            }

            if (!Directory.Exists(Path.Combine(omThemesRootPath, "new-packaging")))
            {
                await _errorHandlingService.ShowErrorAsync(this, "Not an om_themes folder", $"{omThemesRootPath} has no new-packaging folder.");
                return;
            }

            _settingsStore.Save(_settingsStore.Current with { OmThemesRootPath = omThemesRootPath });
        }

        var targetPath = Path.Combine(omThemesRootPath, "new-packaging", viewModel.PackagingSystem ?? "", "themes", viewModel.PackagingName);
        if (viewModel.PackagingSystem is not null
            && Directory.Exists(targetPath)
            && !await _dialogService.ShowConfirmationAsync(
                this,
                "Replace theme in om_themes?",
                $"{targetPath} already exists and will be replaced.",
                DialogVariant.Warning,
                "Replace",
                "Cancel",
                true))
        {
            return;
        }

        if (!viewModel.SendToOmThemes(omThemesRootPath))
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot send to om_themes", viewModel.Status);
        }
    }

    private async void ExportPackage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CurrentDraftPath: not null } viewModel)
        {
            return;
        }

        var outputDirectory = await PickFolderAsync("Select the output folder for the .tar.gz package");
        if (outputDirectory is not null && !viewModel.ExportPackage(outputDirectory))
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot export package", viewModel.Status);
        }
    }

    private void RunValidation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RunValidation();
        }
    }

    private async void CreateTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardDraftAsync())
        {
            return;
        }

        var installedBases = _baseFirmwareManager.GetInstalledBases()
            .Where(info => info.Status == BaseInstallStatus.Valid)
            .ToList();
        var details = await new CreateThemeDialog(installedBases, new AtlasLayoutMatcher()).ShowDialog<NewThemeDetails?>(this);
        if (details is null)
        {
            return;
        }

        var destinationParentPath = await GetThemeDestinationParentPathAsync();
        if (destinationParentPath is null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var destinationPath = Path.Combine(
            destinationParentPath,
            ThemeDirectoryName.FromDisplayName(details.Name));

        var request = new NewThemeRequest(
            details.BaseUiPath,
            destinationPath,
            details.Name,
            details.Author,
            details.AtlasImagePath);

        if (viewModel.CreateTheme(request))
        {
            LoadAtlasImage(viewModel.AtlasImagePath);
            ShowSpritePreview(viewModel.SelectedSprite);
        }
        else
        {
            await _errorHandlingService.ShowErrorAsync(
                this,
                "Cannot create theme",
                viewModel.Status);
        }
    }

    // Wallpapers Handlers
    private void WallpaperList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: WallpaperAsset wallpaper })
        {
            ShowWallpaperPreview(wallpaper);
        }
    }

    private void ShowWallpaperPreview(WallpaperAsset wallpaper)
    {
        WallpaperPreviewImage.Source = null;
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;

        if (File.Exists(wallpaper.ImagePath))
        {
            try
            {
                _wallpaperBitmap = new Bitmap(wallpaper.ImagePath);
                WallpaperPreviewImage.Source = _wallpaperBitmap;
            }
            catch
            {
                // Ignore preview decode failure
            }
        }
    }

    private async void ReplaceWallpaper_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedWallpaper is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select replacement PNG for {viewModel.SelectedWallpaper.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var replacementPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (replacementPath is null)
        {
            return;
        }

        if (viewModel.ReplaceSelectedWallpaper(replacementPath))
        {
            ShowWallpaperPreview(viewModel.SelectedWallpaper);
        }
        else
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot replace wallpaper", viewModel.Status);
        }
    }

    private async void ExportWallpaper_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedWallpaper is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Wallpaper",
            DefaultExtension = "png",
            SuggestedFileName = $"{viewModel.SelectedWallpaper.Name}.png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var savePath = file?.TryGetLocalPath();
        if (savePath != null)
        {
            viewModel.ExportSelectedWallpaper(savePath);
        }
    }

    // Extra Elements Handlers
    private void ExtraAssetList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ExtraAsset asset })
        {
            ShowExtraAssetPreview(asset);
        }
    }

    private void ShowExtraAssetPreview(ExtraAsset asset)
    {
        ExtraAssetPreviewImage.Source = null;
        _extraAssetBitmap?.Dispose();
        _extraAssetBitmap = null;

        if (File.Exists(asset.ImagePath))
        {
            try
            {
                _extraAssetBitmap = new Bitmap(asset.ImagePath);
                ExtraAssetPreviewImage.Source = _extraAssetBitmap;
            }
            catch
            {
                // Ignore preview decode failure
            }
        }
    }

    private async void AddExtraAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select PNG element to add to theme",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var sourcePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (sourcePath is null)
        {
            return;
        }

        var fileName = Path.GetFileName(sourcePath);
        var relativePath = $"resources/sprites/sampleasset/{fileName}";

        if (viewModel.AddExtraAsset(relativePath, sourcePath))
        {
            if (viewModel.SelectedExtraAsset != null)
            {
                ShowExtraAssetPreview(viewModel.SelectedExtraAsset);
            }
        }
        else
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot add element", viewModel.Status);
        }
    }

    private async void ProvideMissingTexture_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedMissingTexture: { } texture } viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select PNG for {Path.GetFileName(texture.ImageRelativePath)}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var sourcePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (sourcePath is not null && !viewModel.ProvideSelectedMissingTexture(sourcePath))
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot add texture", viewModel.Status);
        }
    }

    private async void ReplaceExtraAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedExtraAsset is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select replacement PNG for {viewModel.SelectedExtraAsset.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var replacementPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (replacementPath is null)
        {
            return;
        }

        if (viewModel.ReplaceSelectedExtraAsset(replacementPath))
        {
            ShowExtraAssetPreview(viewModel.SelectedExtraAsset);
        }
        else
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot replace element", viewModel.Status);
        }
    }

    private void DeleteExtraAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DeleteSelectedExtraAsset();
            ExtraAssetPreviewImage.Source = null;
        }
    }

    private async void ExportExtraAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedExtraAsset is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Element",
            DefaultExtension = "png",
            SuggestedFileName = Path.GetFileName(viewModel.SelectedExtraAsset.ImagePath),
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var savePath = file?.TryGetLocalPath();
        if (savePath != null)
        {
            viewModel.ExportSelectedExtraAsset(savePath);
        }
    }

    // Zoom Controls
    private void Zoom1x_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(1);
    private void Zoom2x_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(2);
    private void Zoom4x_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(4);
    private void ZoomFit_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(0);

    private void SetSpriteZoom(int zoomLevel)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SpriteZoom = zoomLevel;
            SpritePreviewImage.Stretch = zoomLevel == 0 ? Avalonia.Media.Stretch.Uniform : Avalonia.Media.Stretch.None;
        }
    }

    private async Task<string?> GetThemeDestinationParentPathAsync()
    {
        var configuredPath = _settingsStore.Current.DefaultThemeDestinationPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return await PickFolderAsync("Select destination parent folder");
        }

        try
        {
            Directory.CreateDirectory(configuredPath);
            return configuredPath;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _errorHandlingService.ShowErrorAsync(
                this,
                "Cannot use theme destination",
                exception.Message);
            return null;
        }
    }

    private async Task<string?> AskForBaseAsync(string themePath, string message, string? currentBaseUiPath = null)
    {
        var installedBases = GetValidBases();
        if (installedBases.Count == 0)
        {
            await ShowBaseManagementAsync();
            installedBases = GetValidBases();
            if (installedBases.Count == 0)
            {
                return null;
            }
        }

        var preferredPath = currentBaseUiPath ?? _baseFirmwareManager.FindCompatibleBaseUiPath(themePath);
        var preferred = installedBases.FirstOrDefault(info => info.LocalPath == preferredPath);
        var selected = await new SelectBaseDialog(installedBases, preferred, message).ShowDialog<InstalledBaseInfo?>(this);
        return selected?.LocalPath;
    }

    private System.Collections.Generic.List<InstalledBaseInfo> GetValidBases()
    {
        return _baseFirmwareManager.GetInstalledBases()
            .Where(info => info.Status == BaseInstallStatus.Valid)
            .ToList();
    }

    private async void ChangeBase_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CurrentDraftPath: not null } viewModel)
        {
            return;
        }

        var baseUiPath = await AskForBaseAsync(
            viewModel.ThemePath,
            "Choose the Base this theme is edited against. Changes in the draft are kept.",
            viewModel.CurrentBaseUiPath);
        if (baseUiPath is not null && viewModel.ChangeBase(baseUiPath))
        {
            LoadAtlasImage(viewModel.AtlasImagePath);
            ShowSpritePreview(viewModel.SelectedSprite);
        }
    }

    private static bool HasAtlasDefinition(string rootPath)
    {
        return File.Exists(Path.Combine(rootPath, "resources", "sprites", "packed.json"));
    }

    private async void ShowHome_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardDraftAsync())
        {
            return;
        }

        DisposePreview();
        _atlasImagePath = "";

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ShowHome();
        }
    }

    private async Task<bool> ConfirmDiscardDraftAsync()
    {
        if (DataContext is not MainWindowViewModel { HasUnsavedChanges: true })
        {
            return true;
        }

        return await _dialogService.ShowConfirmationAsync(
            this,
            "Discard draft?",
            "The unsaved changes in this draft will be lost.",
            DialogVariant.Warning,
            "Discard draft",
            "Keep editing",
            true);
    }

    private void ThemeMode_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = application.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        UpdateThemeModeButton(application.ActualThemeVariant);
    }

    private void Application_ActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (sender is Application application)
        {
            UpdateThemeModeButton(application.ActualThemeVariant);
        }
    }

    private void UpdateThemeModeButton(ThemeVariant themeVariant)
    {
        var isDarkMode = themeVariant == ThemeVariant.Dark;
        ThemeModeIcon.Kind = isDarkMode
            ? LucideIconKind.Sun
            : LucideIconKind.Moon;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void SpriteList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SpriteRegion sprite })
        {
            ShowSpritePreview(sprite);
        }
    }

    private void LoadAtlasImage(string path)
    {
        DisposeAtlasPreview();
        _atlasImagePath = File.Exists(path) ? path : "";
        if (_atlasImagePath.Length == 0)
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(_atlasImagePath);
            _atlasBitmap = new Bitmap(stream);
            AtlasCanvas.Source = _atlasBitmap;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _atlasBitmap = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.AtlasImagePath) && viewModel.AtlasImagePath != _atlasImagePath)
        {
            LoadAtlasImage(viewModel.AtlasImagePath);
            ShowSpritePreview(viewModel.SelectedSprite);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedSprite))
        {
            ShowSpritePreview(viewModel.SelectedSprite);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.AnimationFrames))
        {
            AnimationPreviewImage.Source = null;
            _animationClock.Restart();
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedAnimation.Duration: > 0 } viewModel)
        {
            return;
        }

        var loop = viewModel.AnimationFrames.Where(frame => frame.IsLoopFrame).ToList();
        if (loop.Count == 0)
        {
            return;
        }

        var frameTime = (double)viewModel.FrameTimeMilliseconds / 1000;
        var frame = frameTime > 0
            ? loop[(int)(_animationClock.Elapsed.TotalSeconds / frameTime) % loop.Count]
            : loop[0];
        if (!ReferenceEquals(AnimationPreviewImage.Source, frame.Thumbnail))
        {
            AnimationPreviewImage.Source = frame.Thumbnail;
        }
    }

    private async void ApplyFrameTime_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && !viewModel.ApplyFrameTime())
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot change the frame time", viewModel.Status);
        }
    }

    private async void ReplaceAnimationFrames_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedAnimation: { } animation, SelectedCharacter: { } character } viewModel)
        {
            return;
        }

        var result = await new SpriteSheetDialog(
            new SpriteSheetImporter(),
            $"Frames for {character} {animation.Name}",
            viewModel.CurrentCharacterFrameSize).ShowDialog<SpriteSheetResult?>(this);
        if (result is null)
        {
            return;
        }

        var resize = false;
        if (viewModel.CurrentCharacterFrameSize is { } current
            && (current.Width != result.FrameSize.Width || current.Height != result.FrameSize.Height))
        {
            resize = await _dialogService.ShowConfirmationAsync(
                this,
                "Resize the character?",
                $"The new frames are {result.FrameSize.Width} x {result.FrameSize.Height} but {character} is drawn from {current.Width} x {current.Height} frames. "
                + "Resize the character in the scenes so the new frames are not stretched? Its other animations will then need frames of the same size.",
                DialogVariant.Warning,
                "Resize character",
                "Keep current size",
                false);
        }

        AnimationPreviewImage.Source = null;
        if (!viewModel.ReplaceAnimationFrames(result.FramePaths, result.FrameSize, resize))
        {
            await _errorHandlingService.ShowErrorAsync(this, "Cannot replace frames", viewModel.Status);
        }
    }

    private void AtlasZoom1x_Click(object? sender, RoutedEventArgs e) => AtlasCanvas.Zoom = 1;
    private void AtlasZoom2x_Click(object? sender, RoutedEventArgs e) => AtlasCanvas.Zoom = 2;
    private void AtlasZoom3x_Click(object? sender, RoutedEventArgs e) => AtlasCanvas.Zoom = 3;

    private void ShowSpritePreview(SpriteRegion? sprite)
    {
        SpritePreviewImage.Source = null;
        _spritePreview = null;

        if (_atlasBitmap is null || sprite is null)
        {
            return;
        }

        var frame = sprite.Frame;
        try
        {
            _spritePreview = new CroppedBitmap(
                _atlasBitmap,
                new PixelRect(frame.X, frame.Y, frame.Width, frame.Height));
            SpritePreviewImage.Source = _spritePreview;
        }
        catch
        {
            // Ignore preview decode failure
        }
    }

    private void DisposeAtlasPreview()
    {
        SpritePreviewImage.Source = null;
        AtlasCanvas.Source = null;
        _atlasBitmap?.Dispose();
        _spritePreview = null;
        _atlasBitmap = null;
    }

    private void DisposePreview()
    {
        DisposeAtlasPreview();

        WallpaperPreviewImage.Source = null;
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;

        ExtraAssetPreviewImage.Source = null;
        _extraAssetBitmap?.Dispose();
        _extraAssetBitmap = null;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isCloseConfirmed
            || DataContext is not MainWindowViewModel { IsThemeOpen: true })
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);

        if (_isCloseConfirmationOpen)
        {
            return;
        }

        _isCloseConfirmationOpen = true;
        try
        {
            _isCloseConfirmed = await _dialogService.ShowConfirmationAsync(
                this,
                "Discard open draft?",
                "A theme draft is open. Closing Mini Theme Studio will discard it.",
                DialogVariant.Warning,
                "Discard draft and close",
                "Keep app open",
                true);

            if (_isCloseConfirmed)
            {
                Close();
            }
        }
        finally
        {
            _isCloseConfirmationOpen = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _animationTimer.Stop();
        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged -= Application_ActualThemeVariantChanged;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DiscardTheme();
        }

        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        DisposePreview();
        GC.SuppressFinalize(this);
    }
}
