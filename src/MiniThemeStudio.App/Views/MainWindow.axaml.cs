using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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

namespace MiniThemeStudio.App.Views;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly IDialogService _dialogService;
    private readonly ISpriteSheetImporter _spriteSheetImporter;

    private string _atlasImagePath = "";
    private bool _isCloseConfirmed;
    private bool _isCloseConfirmationOpen;
    private Bitmap? _atlasBitmap;
    private CroppedBitmap? _spritePreview;
    private Bitmap? _unusedImageBitmap;
    private readonly Avalonia.Threading.DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch _animationClock = System.Diagnostics.Stopwatch.StartNew();

    // for the XAML previewer only: the app builds the window through the service container
    public MainWindow()
        : this(null!, null!, null)
    {
    }

    public MainWindow(
        IDialogService dialogService,
        ISpriteSheetImporter spriteSheetImporter,
        MainWindowViewModel? viewModel)
    {
        _dialogService = dialogService;
        _spriteSheetImporter = spriteSheetImporter;
        DataContext = viewModel;

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

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        Opened -= MainWindow_Opened;
        if (DataContext is MainWindowViewModel viewModel && !await viewModel.StartAsync())
        {
            Close();
        }
    }

    private async void ReplaceBgm_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string trackName } || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select audio file for {trackName}",
            AllowMultiple = false,
            FileTypeFilter = [
                new FilePickerFileType("Audio files (*.wav, *.mp3, *.ogg, *.flac, *.m4a)")
                {
                    Patterns = ["*.wav", "*.mp3", "*.ogg", "*.flac", "*.m4a", "*.aac", "*.wma"]
                },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            viewModel.ReplaceBgmTrack(trackName, path);
        }
    }

    private async void ConvertBgmNow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string trackName } || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.ConvertBgmTrackNowAsync(trackName);
    }

    private void RevertBgm_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string trackName } || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.RevertBgmTrack(trackName);
    }

    private void MuteBgm_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string trackName } || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.MuteBgmTrack(trackName);
    }

    private void UnusedImageList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UnusedImagePreviewImage.Source = null;
        _unusedImageBitmap?.Dispose();
        _unusedImageBitmap = LoadPreview((sender as ListBox)?.SelectedItem as ExtraAsset);
        UnusedImagePreviewImage.Source = _unusedImageBitmap;
    }

    private static Bitmap? LoadPreview(ExtraAsset? asset)
    {
        if (asset is null || !File.Exists(asset.ImagePath))
        {
            return null;
        }

        try
        {
            return new Bitmap(asset.ImagePath);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private async void DeleteUnusedImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedUnusedImage: { } image } viewModel)
        {
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Delete unused image?",
            image.OverridesBase
                ? $"No scene draws resources/{image.RelativePath}. Its replacement is removed and the Base image is kept."
                : $"No scene draws resources/{image.RelativePath}. It is removed from the theme.",
            DialogVariant.Warning,
            "Delete",
            "Keep",
            true);
        if (confirmed && !viewModel.DeleteSelectedUnusedImage())
        {
            await ShowErrorAsync("Cannot delete image", viewModel.Status);
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
            await ShowErrorAsync("Cannot add texture", viewModel.Status);
        }
    }

    // Zoom Controls
    private void Zoom1x_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(1);
    private void Zoom2x_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(2);
    private void Zoom4x_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(4);
    private void ZoomFit_Click(object? sender, RoutedEventArgs e) => SetSpriteZoom(0);

    // 0 fits the sprite in the preview
    private void SetSpriteZoom(int zoomLevel)
    {
        SpritePreviewImage.Stretch = zoomLevel == 0 ? Avalonia.Media.Stretch.Uniform : Avalonia.Media.Stretch.None;
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
            viewModel.SpritesTab.PropertyChanged += SpritesTab_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsThemeOpen) && !viewModel.IsThemeOpen)
        {
            DisposePreview();
            _atlasImagePath = "";
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.AnimationFrames))
        {
            AnimationPreviewImage.Source = null;
            _animationClock.Restart();
        }
    }

    private void SpritesTab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not SpritesViewModel sprites)
        {
            return;
        }

        if (e.PropertyName is nameof(SpritesViewModel.AtlasImagePath) or nameof(SpritesViewModel.AtlasRevision))
        {
            LoadAtlasImage(sprites.AtlasImagePath);
            ShowSpritePreview(sprites.SelectedSprite);
        }
        else if (e.PropertyName == nameof(SpritesViewModel.SelectedSprite))
        {
            ShowSpritePreview(sprites.SelectedSprite);
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

    private async void CompleteFont_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && !viewModel.CompleteSelectedFont())
        {
            await ShowErrorAsync("Cannot fill the font", viewModel.Status);
        }
    }

    private async void ApplyPaletteColor_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && !viewModel.ApplyPaletteColor())
        {
            await ShowErrorAsync("Cannot change the colour", viewModel.Status);
        }
    }

    private async void ApplyFrameTime_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && !viewModel.ApplyFrameTime())
        {
            await ShowErrorAsync("Cannot change the frame time", viewModel.Status);
        }
    }

    private void MirrorPreview_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        AnimationPreviewImage.RenderTransform = MirrorPreviewCheckBox.IsChecked == true ? new ScaleTransform(-1, 1) : null;
    }

    private async void MirrorSelectedFrames_Click(object? sender, RoutedEventArgs e)
    {
        var textureIds = AnimationFramesList.SelectedItems?.OfType<AnimationFrameView>().Select(frame => frame.TextureId).ToList() ?? [];
        await MirrorFramesAsync(textureIds);
    }

    private async void MirrorAllFrames_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { SelectedAnimation: { } animation })
        {
            await MirrorFramesAsync(animation.Tracks.SelectMany(track => track.Keys).Select(key => key.TextureId).ToList());
        }
    }

    private async Task MirrorFramesAsync(IReadOnlyCollection<string> textureIds)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        AnimationPreviewImage.Source = null;
        if (viewModel.MirrorFrames(textureIds))
        {
            MirrorPreviewCheckBox.IsChecked = false;
        }
        else
        {
            await ShowErrorAsync("Cannot flip frames", viewModel.Status);
        }
    }

    private async void ReplaceAnimationFrames_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedAnimation: { } animation, SelectedCharacter: { } character } viewModel)
        {
            return;
        }

        var result = await new SpriteSheetDialog(
            _spriteSheetImporter,
            $"Frames for {character} {animation.Name}",
            viewModel.CurrentCharacterFrameSize,
            viewModel.AnimationFrames.FirstOrDefault(frame => frame.IsLoopFrame)?.Thumbnail).ShowDialog<SpriteSheetResult?>(this);
        if (result is null)
        {
            return;
        }

        var resize = false;
        if (viewModel.CurrentCharacterFrameSize is { } current
            && (current.Width != result.FrameSize.Width || current.Height != result.FrameSize.Height))
        {
            resize = await _dialogService.ShowConfirmationAsync(
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
            await ShowErrorAsync("Cannot replace frames", viewModel.Status);
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

        UnusedImagePreviewImage.Source = null;
        _unusedImageBitmap?.Dispose();
        _unusedImageBitmap = null;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isCloseConfirmed || DataContext is not MainWindowViewModel viewModel)
        {
            base.OnClosing(e);
            return;
        }

        // the dialog is asynchronous, so the close is cancelled now and repeated once the user has chosen
        e.Cancel = true;
        base.OnClosing(e);
        if (_isCloseConfirmationOpen)
        {
            return;
        }

        _isCloseConfirmationOpen = true;
        try
        {
            if (await viewModel.ConfirmCloseAsync())
            {
                _isCloseConfirmed = true;
                Close();
            }
        }
        finally
        {
            _isCloseConfirmationOpen = false;
        }
    }

    private Task ShowErrorAsync(string title, string message)
    {
        return _dialogService.ShowInformationAsync(title, message, DialogVariant.Error);
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

    private async void ImportTrueTypeFont_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedFont is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select TTF, OTF or ZIP font file to convert for {viewModel.SelectedFont.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Font or Zip package") { Patterns = ["*.ttf", "*.otf", "*.zip"] },
                new FilePickerFileType("TrueType / OpenType font") { Patterns = ["*.ttf", "*.otf"] },
                new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }
            ]
        });

        var fontPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (fontPath is null)
        {
            return;
        }

        if (!viewModel.ImportSelectedTrueTypeFont(fontPath))
        {
            await ShowErrorAsync("Cannot convert font", viewModel.Status);
        }
    }

    private async void ReplaceFontTexture_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedFont is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select replacement PNG texture for {viewModel.SelectedFont.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var replacementPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (replacementPath is null)
        {
            return;
        }

        if (!viewModel.ReplaceSelectedFontTexture(replacementPath))
        {
            await ShowErrorAsync("Cannot replace font texture", viewModel.Status);
        }
    }

    private async void ReplaceFontFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedFont is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select replacement font file for {viewModel.SelectedFont.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Console Font file") { Patterns = ["*.fnt", "*.font"] }]
        });

        var replacementPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (replacementPath is null)
        {
            return;
        }

        if (!viewModel.ReplaceSelectedFontFile(replacementPath))
        {
            await ShowErrorAsync("Cannot replace font file", viewModel.Status);
        }
    }

    private async void ExportFontTexture_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedFont is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export font texture {viewModel.SelectedFont.DisplayName}",
            DefaultExtension = "png",
            SuggestedFileName = $"{viewModel.SelectedFont.Name}_texture.png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
        });

        var destinationPath = file?.TryGetLocalPath();
        if (destinationPath is null)
        {
            return;
        }

        if (!viewModel.ExportSelectedFontTexture(destinationPath))
        {
            await ShowErrorAsync("Cannot export font texture", viewModel.Status);
        }
    }

    private async void ExportFontFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedFont is null)
        {
            return;
        }

        var ext = Path.GetExtension(viewModel.SelectedFont.FilePath);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export font file {viewModel.SelectedFont.DisplayName}",
            DefaultExtension = ext.TrimStart('.'),
            SuggestedFileName = $"{viewModel.SelectedFont.Name}{ext}",
            FileTypeChoices = [new FilePickerFileType("Console Font file") { Patterns = ["*.fnt", "*.font"] }]
        });

        var destinationPath = file?.TryGetLocalPath();
        if (destinationPath is null)
        {
            return;
        }

        if (!viewModel.ExportSelectedFontFile(destinationPath))
        {
            await ShowErrorAsync("Cannot export font", viewModel.Status);
        }
    }

    public void Dispose()
    {
        DisposePreview();
        GC.SuppressFinalize(this);
    }
}
