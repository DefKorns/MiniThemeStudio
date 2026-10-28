using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Lucide.Avalonia;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class MainWindow : Window, IDisposable
{

    private string _atlasImagePath = "";
    private bool _isCloseConfirmed;
    private bool _isCloseConfirmationOpen;
    private Bitmap? _atlasBitmap;
    private CroppedBitmap? _spritePreview;
    private readonly Avalonia.Threading.DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch _animationClock = System.Diagnostics.Stopwatch.StartNew();

    // for the XAML previewer only: the app builds the window through the service container
    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(MainWindowViewModel? viewModel)
    {
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
        bool isDarkMode = themeVariant == ThemeVariant.Dark;
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
            using FileStream stream = File.OpenRead(_atlasImagePath);
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
            viewModel.CharactersTab.PropertyChanged += CharactersTab_PropertyChanged;
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

    // the animation preview starts again from its first frame when the frames change
    private void CharactersTab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharactersViewModel.AnimationFrames))
        {
            AnimationPreviewImage.Source = null;
            _animationClock.Restart();
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CharactersTab: { SelectedAnimation.Duration: > 0 } viewModel })
        {
            return;
        }

        var loop = viewModel.AnimationFrames.Where(frame => frame.IsLoopFrame).ToList();
        if (loop.Count == 0)
        {
            return;
        }

        double frameTime = (double)viewModel.FrameTimeMilliseconds / 1000;
        AnimationFrameView frame = frameTime > 0
            ? loop[(int)(_animationClock.Elapsed.TotalSeconds / frameTime) % loop.Count]
            : loop[0];
        if (!ReferenceEquals(AnimationPreviewImage.Source, frame.Thumbnail))
        {
            AnimationPreviewImage.Source = frame.Thumbnail;
        }
    }

    private void MirrorPreview_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        AnimationPreviewImage.RenderTransform = MirrorPreviewCheckBox.IsChecked == true ? new ScaleTransform(-1, 1) : null;
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

        AtlasRectangle frame = sprite.Frame;
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
