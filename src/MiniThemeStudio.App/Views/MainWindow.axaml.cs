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
    private readonly IConsoleBaseUiImporter _consoleBaseUiImporter;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IBaseUiValidator _baseUiValidator;
    private readonly IBaseFirmwareManager _baseFirmwareManager;
    private readonly IThemeAssetManager _themeAssetManager;
    private readonly IThemeValidator _themeValidator;
    private readonly LocalBaseUiImporter _localBaseUiImporter;

    private string _atlasImagePath = "";
    private bool _isCloseConfirmed;
    private bool _isCloseConfirmationOpen;
    private Bitmap? _previewBitmap;
    private CroppedBitmap? _spritePreview;
    private Bitmap? _wallpaperBitmap;
    private Bitmap? _extraAssetBitmap;

    public MainWindow()
        : this(
            new JsonAppSettingsStore(),
            new ConsoleBaseUiImporter(new BaseUiValidator(), new ConsoleDirectoryClientFactory()))
    {
    }

    public MainWindow(
        IAppSettingsStore settingsStore,
        IConsoleBaseUiImporter consoleBaseUiImporter,
        IBaseUiValidator? baseUiValidator = null,
        IBaseFirmwareManager? baseFirmwareManager = null,
        IThemeAssetManager? themeAssetManager = null,
        IThemeValidator? themeValidator = null)
    {
        _settingsStore = settingsStore;
        _consoleBaseUiImporter = consoleBaseUiImporter;
        _baseUiValidator = baseUiValidator ?? new BaseUiValidator();
        _localBaseUiImporter = new LocalBaseUiImporter(_baseUiValidator);
        _baseFirmwareManager = baseFirmwareManager ?? new BaseFirmwareManager(new FirmwareRegistry(), _baseUiValidator, _settingsStore.Current.BaseUiRootPath);
        _themeAssetManager = themeAssetManager ?? new ThemeAssetManager();
        _themeValidator = themeValidator ?? new ThemeValidator();

        _dialogService = new DialogService();
        _errorHandlingService = new ErrorHandlingService(_dialogService);
        InitializeComponent();
        Opened += MainWindow_Opened;

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

    private void ManageBases_Click(object? sender, RoutedEventArgs e)
    {
        new BaseManagementDialog(_baseFirmwareManager).ShowDialog(this);
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        Opened -= MainWindow_Opened;
        var settings = _settingsStore.Current;
        if (settings.InitialSetupCompleted && ContainsValidBaseUi(settings.BaseUiRootPath))
        {
            return;
        }

        if (!await ShowSettingsAsync(true))
        {
            Close();
        }
    }

    private async Task<bool> ShowSettingsAsync(bool isInitialSetup)
    {
        var settings = await new SettingsDialog(
            _settingsStore.Current,
            _consoleBaseUiImporter,
            _baseUiValidator,
            _localBaseUiImporter,
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
            var atlasDefinitionRootPath = FindCachedBaseUiPath(path);
            if (!HasAtlasDefinition(path) && atlasDefinitionRootPath is null)
            {
                atlasDefinitionRootPath = await GetBaseUiPathAsync();
                if (atlasDefinitionRootPath is null)
                {
                    return;
                }
            }

            if (viewModel.OpenTheme(path, atlasDefinitionRootPath))
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

        var details = await new CreateThemeDialog().ShowDialog<NewThemeDetails?>(this);
        if (details is null)
        {
            return;
        }

        var baseThemePath = FindAvailableBaseUiPath(_settingsStore.Current.BaseUiRootPath)
            ?? await GetBaseUiPathAsync();
        if (baseThemePath is null)
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
            baseThemePath,
            destinationPath,
            details.Name,
            details.Author);

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

    private async Task<string?> GetBaseUiPathAsync()
    {
        var settings = _settingsStore.Current;
        var consoleHost = settings.ConsoleConnectionMode == ConsoleConnectionMode.Usb
            ? "169.254.13.37"
            : settings.ConsoleHost;
        var connection = new ConsoleConnectionOptions(consoleHost);
        using var progressDialog = new ConsoleConnectionDialog(cancellationToken =>
            _consoleBaseUiImporter.ImportAvailableAsync(
                settings.BaseUiRootPath,
                connection,
                cancellationToken));
        await progressDialog.ShowDialog<bool>(this);

        if (progressDialog.Result is { } result)
        {
            return result.LocalPath;
        }

        if (progressDialog.Error is null)
        {
            return null;
        }

        await _dialogService.ShowInformationAsync(
            this,
            "Console unavailable",
            $"A Base UI could not be retrieved from the console. Select a local Base UI folder instead.\n\n{progressDialog.Error.Message}",
            DialogVariant.Warning);

        var sourcePath = await PickFolderAsync("Select local Base UI folder");
        if (sourcePath is null)
        {
            return null;
        }

        try
        {
            return _localBaseUiImporter.Import(sourcePath, settings.BaseUiRootPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ArgumentException)
        {
            await _errorHandlingService.ShowErrorAsync(
                this,
                "Cannot import Base UI",
                exception.Message);
            return null;
        }
    }

    private string? FindCachedBaseUiPath(string themePath)
    {
        var baseUiRootPath = _settingsStore.Current.BaseUiRootPath;
        if (HasAtlasDefinition(baseUiRootPath))
        {
            return baseUiRootPath;
        }

        var normalizedThemePath = themePath.Replace('\\', '/');
        var preferredFolderNames = normalizedThemePath.Contains("shonen", StringComparison.OrdinalIgnoreCase)
            ? new[] { "shonen", "shonen/jpn/3.0.1" }
            : normalizedThemePath.Contains("snes", StringComparison.OrdinalIgnoreCase)
                ? new[] { "snes-eur", "snes-usa", "snes-jpn", "snes/eur/2.0.14", "snes/usa/2.0.14", "snes/jpn/2.0.12" }
                : new[] { "nes", "famicom", "nes/usa/1.0.2", "famicom/jpn/1.0.5" };

        foreach (var folderName in preferredFolderNames)
        {
            var candidatePath = Path.Combine(baseUiRootPath, folderName);
            if (HasAtlasDefinition(candidatePath))
            {
                return candidatePath;
            }
        }

        return FindAvailableBaseUiPath(baseUiRootPath, HasAtlasDefinition);
    }

    private string? FindAvailableBaseUiPath(string rootPath)
    {
        return FindAvailableBaseUiPath(rootPath, IsValidBaseUi);
    }

    private static string? FindAvailableBaseUiPath(
        string rootPath,
        Func<string, bool> isAvailable)
    {
        if (isAvailable(rootPath))
        {
            return rootPath;
        }

        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        return Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories).FirstOrDefault(isAvailable);
    }

    private bool ContainsValidBaseUi(string rootPath)
    {
        if (IsValidBaseUi(rootPath))
        {
            return true;
        }

        foreach (var definition in BaseUiCatalog.All)
        {
            if (IsValidBaseUi(Path.Combine(rootPath, definition.LocalFolderName)))
            {
                return true;
            }
        }

        return Directory.Exists(rootPath)
            && Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories).Any(IsValidBaseUi);
    }

    private bool IsValidBaseUi(string path)
    {
        try
        {
            _baseUiValidator.Validate(path);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ArgumentException)
        {
            return false;
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
        DisposePreview();
        _atlasImagePath = File.Exists(path) ? path : "";
    }

    private void ShowSpritePreview(SpriteRegion? sprite)
    {
        SpritePreviewImage.Source = null;
        _spritePreview?.Dispose();
        _previewBitmap?.Dispose();
        _spritePreview = null;
        _previewBitmap = null;

        if (_atlasImagePath.Length == 0 || sprite is null)
        {
            return;
        }

        var frame = sprite.Frame;
        try
        {
            _previewBitmap = new Bitmap(_atlasImagePath);
            _spritePreview = new CroppedBitmap(
                _previewBitmap,
                new PixelRect(frame.X, frame.Y, frame.Width, frame.Height));
            SpritePreviewImage.Source = _spritePreview;
        }
        catch
        {
            // Ignore preview decode failure
        }
    }

    private void DisposePreview()
    {
        SpritePreviewImage.Source = null;
        _spritePreview?.Dispose();
        _previewBitmap?.Dispose();
        _spritePreview = null;
        _previewBitmap = null;

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
