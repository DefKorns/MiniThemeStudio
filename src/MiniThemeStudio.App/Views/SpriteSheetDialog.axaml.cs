using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed record SpriteSheetResult(IReadOnlyList<string> FramePaths, AtlasSize FrameSize);

public sealed partial class SpriteSheetDialog : Window
{
    private readonly ISpriteSheetImporter _importer;
    private readonly AtlasSize? _currentFrameSize;
    private readonly List<uint> _colours = [];
    private readonly List<SpriteRegion> _marked = [];
    private readonly List<CroppedBitmap> _previewFrames = [];
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _previewIndex;
    private string? _sheetPath;
    private Bitmap? _sheetBitmap;

    public SpriteSheetDialog()
        : this(null!, "Import frames from a sprite sheet", null, null)
    {
    }

    public SpriteSheetDialog(ISpriteSheetImporter importer, string title, AtlasSize? currentFrameSize, Bitmap? referenceFrame)
    {
        _importer = importer;
        _currentFrameSize = currentFrameSize;
        InitializeComponent();
        TitleText.Text = title;
        ReferenceImage.Source = referenceFrame;
        DirectionComboBox.SelectionChanged += Direction_SelectionChanged;
        _previewTimer.Tick += PreviewTimer_Tick;
        _previewTimer.Start();
        SheetCanvas.AtlasPointerPressed += SheetCanvas_AtlasPointerPressed;
        SheetCanvas.PropertyChanged += SheetCanvas_PropertyChanged;
        PickColourButton.IsCheckedChanged += (_, _) => SheetCanvas.IsSelectionEnabled = PickColourButton.IsChecked != true;
    }

    private async void OpenSheet_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open sprite sheet",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null)
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            _marked.Clear();
            RefreshPreview();
            SheetCanvas.Source = null;
            _sheetBitmap?.Dispose();
            _sheetBitmap = bitmap;
            _sheetPath = path;
            SheetCanvas.Source = bitmap;
            _colours.Clear();
            _colours.AddRange(_importer.SuggestBackgroundColours(path));
            PickColourButton.IsEnabled = true;
            ClearColoursButton.IsEnabled = true;
            ClearSelectionButton.IsEnabled = true;
            Detect();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            SelectionText.Text = exception.Message;
        }
    }

    private void SheetCanvas_AtlasPointerPressed(object? sender, PixelPoint point)
    {
        if (PickColourButton.IsChecked != true || _sheetPath is null || _sheetBitmap is null
            || point.X >= _sheetBitmap.PixelSize.Width || point.Y >= _sheetBitmap.PixelSize.Height)
        {
            return;
        }

        var colour = _importer.GetPixel(_sheetPath, point.X, point.Y);
        if (!_colours.Contains(colour))
        {
            _colours.Add(colour);
            Detect();
        }

        PickColourButton.IsChecked = false;
    }

    private void SheetCanvas_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Controls.AtlasView.SelectedSpriteProperty || SheetCanvas.SelectedSprite is not { } frame)
        {
            return;
        }

        if (!_marked.Remove(frame))
        {
            _marked.Add(frame);
        }

        SheetCanvas.SelectedSprite = null;
        UpdateSelection();
    }

    private void Detect()
    {
        if (_sheetPath is null)
        {
            return;
        }

        var frames = _importer.DetectFrames(_sheetPath, Keying());
        SheetCanvas.Sprites = frames
            .Select((frame, index) => new SpriteRegion($"#{index + 1}", frame, new AtlasPoint(0, 0), new AtlasSize(frame.Width, frame.Height), new AtlasPoint(0, 0)))
            .ToList();
        _marked.Clear();
        RefreshColourChips();
        UpdateSelection();
    }

    private SheetKeying Keying() => new(_colours.ToList(), (int)(ToleranceBox.Value ?? 8));

    private void RefreshColourChips()
    {
        ColourChips.Children.Clear();
        foreach (var colour in _colours)
        {
            var chip = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromUInt32(colour)),
            };
            ToolTip.SetTip(chip, $"#{colour & 0xFFFFFF:X6} is transparent. Click to keep it.");
            chip.Click += (_, _) =>
            {
                _colours.Remove(colour);
                Detect();
            };
            ColourChips.Children.Add(chip);
        }
    }

    private bool Mirror => DirectionComboBox.SelectedIndex == 1;

    private void Direction_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ImportPreviewImage.RenderTransform = Mirror ? new ScaleTransform(-1, 1) : null;
    }

    private void RefreshPreview()
    {
        ImportPreviewImage.Source = null;
        _previewFrames.Clear();
        _previewIndex = 0;
        if (_sheetBitmap is null || _marked.Count == 0)
        {
            return;
        }

        _previewFrames.AddRange(_marked.Select(region => new CroppedBitmap(
            _sheetBitmap,
            new PixelRect(region.Frame.X, region.Frame.Y, region.Frame.Width, region.Frame.Height))));
        ImportPreviewCanvas.Width = _marked.Max(region => region.Frame.Width);
        ImportPreviewCanvas.Height = _marked.Max(region => region.Frame.Height);
        PreviewTimer_Tick(null, EventArgs.Empty);
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_previewFrames.Count == 0)
        {
            return;
        }

        _previewIndex %= _previewFrames.Count;
        var frame = _marked[_previewIndex].Frame;
        ImportPreviewImage.Width = frame.Width;
        ImportPreviewImage.Height = frame.Height;
        ImportPreviewImage.VerticalAlignment = AnchorComboBox.SelectedIndex == 1 ? Avalonia.Layout.VerticalAlignment.Center : Avalonia.Layout.VerticalAlignment.Bottom;
        ImportPreviewImage.Source = _previewFrames[_previewIndex];
        _previewIndex++;
    }

    private void UpdateSelection()
    {
        SheetCanvas.MarkedSprites = _marked.ToList();
        RefreshPreview();
        UseFramesButton.IsEnabled = _marked.Count > 0;
        var detected = SheetCanvas.Sprites?.Count ?? 0;
        if (_marked.Count == 0)
        {
            SelectionText.Text = detected == 0
                ? "No frames found. Use Pick background colour on every background colour that is still visible."
                : $"{detected} frames detected. Click them in playback order. If a frame swallows its neighbours, pick the background colour around it.";
            return;
        }

        var width = _marked.Max(frame => frame.Frame.Width);
        var height = _marked.Max(frame => frame.Frame.Height);
        var current = _currentFrameSize is { } size && (size.Width != width || size.Height != height)
            ? $" The current frames are {size.Width} x {size.Height}."
            : "";
        SelectionText.Text = $"{_marked.Count} frames selected, exported as {width} x {height}.{current}";
    }

    private void ClearColours_Click(object? sender, RoutedEventArgs e)
    {
        _colours.Clear();
        Detect();
    }

    private void ClearSelection_Click(object? sender, RoutedEventArgs e)
    {
        _marked.Clear();
        UpdateSelection();
    }

    private void Tolerance_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_sheetPath is not null)
        {
            Detect();
        }
    }

    private void Zoom1x_Click(object? sender, RoutedEventArgs e) => SheetCanvas.Zoom = 1;
    private void Zoom2x_Click(object? sender, RoutedEventArgs e) => SheetCanvas.Zoom = 2;
    private void Zoom3x_Click(object? sender, RoutedEventArgs e) => SheetCanvas.Zoom = 3;

    private void UseFrames_Click(object? sender, RoutedEventArgs e)
    {
        if (_sheetPath is null || _marked.Count == 0)
        {
            return;
        }

        try
        {
            var outputDirectory = Path.Combine(Path.GetTempPath(), "MiniThemeStudio", "frames", Guid.NewGuid().ToString("N"));
            var anchor = AnchorComboBox.SelectedIndex == 1 ? FrameAnchor.Center : FrameAnchor.BottomCenter;
            var frames = _marked.Select(frame => frame.Frame).ToList();
            var paths = _importer.ExportFrames(_sheetPath, frames, Keying(), anchor, null, outputDirectory, Mirror);
            Close(new SpriteSheetResult(paths, new AtlasSize(frames.Max(frame => frame.Width), frames.Max(frame => frame.Height))));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            SelectionText.Text = exception.Message;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewTimer.Stop();
        _marked.Clear();
        RefreshPreview();
        SheetCanvas.Source = null;
        _sheetBitmap?.Dispose();
        base.OnClosed(e);
    }
}
