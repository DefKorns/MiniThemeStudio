using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// a colour of the background, edited in place and written with Apply
public sealed partial class BackgroundColorRow(string colorName, string label, Color color) : ObservableObject
{
    public string ColorName { get; } = colorName;

    public string Label { get; } = label;

    public Color Saved { get; } = color;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private Color color = color;

    public bool IsChanged => Color != Saved;
}

// a white image the script tints with the colours: the dot of the patterns, the small characters, the transition
public sealed record BackgroundImageRow(string Name, string Label, string Description, string RelativePath, string ImagePath, int Width, int Height, int Revision)
{
    public string SizeText => string.Create(CultureInfo.InvariantCulture, $"{Width} × {Height}");
}

// The background of the NES and Famicom UIs, in the Wallpapers tab: a script draws it with patterns of dots, the
// small Mario and Luigi and transitions, tinted with three colours of the palette, so there is no image to swap,
// only those colours and the white pictures the script uses.
public sealed partial class PixelBackgroundViewModel : ViewModelBase
{
    private const string BackgroundColor = "bg_base";

    private static readonly (string Name, string Label)[] BackgroundColors =
    [
        (BackgroundColor, "Background"),
        ("bg_mario", "Mario's colour"),
        ("bg_luigi", "Luigi's colour"),
    ];

    private static readonly (string Name, string Label, string Description)[] BackgroundImages =
    [
        ("wallpaper-dots", "Dot", "The square the patterns are drawn with, one per pixel of the picture."),
        ("wallpaper-hvc", "Mario and Luigi", "The small characters that walk over the background."),
        ("wallpaper-transit", "Transition shapes", "The shapes that grow when one picture gives way to the next."),
    ];

    private readonly ThemeSession _session;
    private readonly IThemePaletteEditor _paletteEditor;
    private readonly IThemeAssetManager _assetManager;
    private readonly IFileDialogService _fileDialogs;
    private readonly IErrorHandler _errorHandler;
    private int _revision;

    public PixelBackgroundViewModel(ThemeSession session, IThemePaletteEditor paletteEditor, IThemeAssetManager assetManager, IFileDialogService fileDialogs, IErrorHandler errorHandler)
    {
        _session = session;
        _paletteEditor = paletteEditor;
        _assetManager = assetManager;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPixelBackground))]
    private IReadOnlyList<BackgroundColorRow> colors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPixelBackground))]
    private IReadOnlyList<BackgroundImageRow> images = [];

    // what the images are shown over, as the console tints them
    [ObservableProperty]
    private IBrush backgroundBrush = Brushes.Black;

    public bool HasPixelBackground => Images.Count > 0;

    [RelayCommand]
    private async Task ApplyColorsAsync()
    {
        if (_session.Draft is not { } draft || Colors.Where(row => row.IsChanged).ToList() is not { Count: > 0 } changed)
        {
            return;
        }

        var palettes = _paletteEditor.GetPalettes(draft.DraftPath, _session.BaseUiPath);
        if (await _errorHandler.TryRunAsync("Cannot change the background colours", () =>
        {
            foreach (var row in changed)
            {
                var targets = palettes.Where(palette => palette.Colors.Any(color => color.Name == row.ColorName)).Select(palette => palette.RelativePath).ToList();
                _paletteEditor.SetColor(draft.DraftPath, _session.BaseUiPath, PaletteColor.FromBytes(row.ColorName, row.Color.R, row.Color.G, row.Color.B, row.Color.A), targets);
            }

            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Changed {string.Join(", ", changed.Select(row => row.Label.ToLowerInvariant()))}.";
        }
    }

    [RelayCommand]
    private void ResetColors()
    {
        foreach (var row in Colors)
        {
            row.Color = row.Saved;
        }
    }

    // a new picture of the same size: the script cuts it at fixed places
    [RelayCommand]
    private async Task ReplaceImageAsync(BackgroundImageRow? image)
    {
        if (_session.Draft is not { } draft || image is null
            || await _fileDialogs.OpenFileAsync($"Select the new {image.Label} ({image.SizeText} PNG)", new FileType("PNG image", "*.png")) is not { } imagePath)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot replace {image.Label}", () =>
        {
            using (var picture = new Bitmap(imagePath))
            {
                if (picture.PixelSize.Width != image.Width || picture.PixelSize.Height != image.Height)
                {
                    throw new OperationFailedException(string.Create(CultureInfo.InvariantCulture,
                        $"{image.Label} must be {image.Width} × {image.Height} pixels, like the image it replaces; {Path.GetFileName(imagePath)} is {picture.PixelSize.Width} × {picture.PixelSize.Height}."));
                }
            }

            _assetManager.ReplaceExtraAsset(Path.Combine(draft.DraftPath, "resources", image.RelativePath), imagePath, Path.ChangeExtension(image.ImagePath, ".json"));
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Replaced {image.Label}.";
        }
    }

    [RelayCommand]
    private async Task ExportImageAsync(BackgroundImageRow? image)
    {
        if (image is null
            || await _fileDialogs.SaveFileAsync($"Export {image.Label}", $"{image.Name}.png", "png", new FileType("PNG image", "*.png")) is not { } savePath)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot export {image.Label}", () =>
        {
            File.Copy(image.ImagePath, savePath, true);
            return Task.CompletedTask;
        }))
        {
            _session.Status = $"Exported {image.Label} to {Path.GetFileName(savePath)}.";
        }
    }

    private void Reload()
    {
        if (_session.Draft is not { } draft)
        {
            Colors = [];
            Images = [];
            return;
        }

        // the image rows are new each time, so the views read the files again after a change
        _revision++;
        var roots = new[] { draft.DraftPath, _session.BaseUiPath }.Where(root => root is not null).Select(root => root!).ToList();
        Images = ReadImages(roots).ToList();

        var palette = Images.Count == 0 ? [] : _paletteEditor.GetPalettes(draft.DraftPath, _session.BaseUiPath).SelectMany(candidate => candidate.Colors).ToList();
        Colors = ReadColors(palette).ToList();
        BackgroundBrush = Colors.FirstOrDefault(row => row.ColorName == BackgroundColor) is { } background
            ? new ImmutableSolidColorBrush(background.Saved)
            : Brushes.Black;
    }

    private IEnumerable<BackgroundImageRow> ReadImages(List<string> roots)
    {
        foreach (var (name, label, description) in BackgroundImages)
        {
            var relativePath = $"sprites/{name}.png";
            if (roots.Select(root => Path.Combine(root, "resources", relativePath)).FirstOrDefault(File.Exists) is not { } imagePath)
            {
                continue;
            }

            using var picture = new Bitmap(imagePath);
            yield return new BackgroundImageRow(name, label, description, relativePath, imagePath, picture.PixelSize.Width, picture.PixelSize.Height, _revision);
        }
    }

    private static IEnumerable<BackgroundColorRow> ReadColors(List<PaletteColor> palette)
    {
        foreach (var (name, label) in BackgroundColors)
        {
            if (palette.FirstOrDefault(color => color.Name == name) is { } color)
            {
                yield return new BackgroundColorRow(name, label, Color.FromArgb(
                    PaletteColor.ChannelToByte(color.Alpha),
                    PaletteColor.ChannelToByte(color.Red),
                    PaletteColor.ChannelToByte(color.Green),
                    PaletteColor.ChannelToByte(color.Blue)));
            }
        }
    }
}
