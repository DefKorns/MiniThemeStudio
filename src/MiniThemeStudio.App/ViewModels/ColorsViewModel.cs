using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Controls;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// IsModified: some region's value of the colour is not the Base's
public sealed record PaletteColorRow(string Name, IBrush Swatch, string Hex, PaletteColor Color, bool IsModified = false)
{
    public string Title => PaletteColorNames.Title(Name);
}

// The Colors tab: the colours of the Lua palettes of each region and where the scenes use each colour.
public sealed partial class ColorsViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly IThemePaletteEditor _paletteEditor;
    private readonly IErrorHandler _errorHandler;
    private readonly ISceneEditor _sceneEditor;
    private readonly SceneImageLibrary _images;
    private readonly PixelBackgroundViewModel _pixelBackground;
    private HashSet<string> _modifiedNames = [];

    public ColorsViewModel(ThemeSession session, IThemePaletteEditor paletteEditor, IErrorHandler errorHandler, ISceneEditor sceneEditor, SceneImageLibrary images, PixelBackgroundViewModel pixelBackground)
    {
        _pixelBackground = pixelBackground;
        _session = session;
        _paletteEditor = paletteEditor;
        _errorHandler = errorHandler;
        _sceneEditor = sceneEditor;
        _images = images;
        _session.Reloaded += (_, _) => Reload();
    }

    [ObservableProperty]
    private IReadOnlyList<ThemePalette> palettes = [];

    [ObservableProperty]
    private ThemePalette? selectedPalette;

    [ObservableProperty]
    private string regionsText = "";

    [ObservableProperty]
    private IReadOnlyList<PaletteColorRow> paletteColors = [];

    [ObservableProperty]
    private PaletteColorRow? selectedPaletteColor;

    [ObservableProperty]
    private Color editColor;

    private IReadOnlyList<PaletteColorUsage> _allColorUsages = [];

    // a palette carries every colour of the engine, many of them for other consoles' UIs
    [ObservableProperty]
    private bool showUnusedColors;

    partial void OnShowUnusedColorsChanged(bool value) => OnSelectedPaletteChanged(SelectedPalette);

    [ObservableProperty]
    private IReadOnlyList<PaletteColorUsage> colorUsages = [];

    [ObservableProperty]
    private string colorUsageSummary = "";

    // the scene that shows where the selected colour is used, with the palette's colours (the one being edited live)
    [ObservableProperty]
    private SceneNode? previewRoot;

    [ObservableProperty]
    private SceneImageSource previewImages = SceneImageSource.Empty;

    [ObservableProperty]
    private IReadOnlyDictionary<string, Color> previewColors = new Dictionary<string, Color>();

    [ObservableProperty]
    private IReadOnlyList<string> previewScenes = [];

    [ObservableProperty]
    private string? selectedPreviewScene;

    public bool HasPreviewScenes => PreviewScenes.Count > 1;

    public bool HasPreview => PreviewRoot is not null;

    partial void OnPreviewRootChanged(SceneNode? value) => OnPropertyChanged(nameof(HasPreview));

    public bool CanResetColor => SelectedPaletteColor is { IsModified: true };

    public bool CanResetAllColors => _modifiedNames.Count > 0;

    partial void OnPreviewScenesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasPreviewScenes));

    partial void OnEditColorChanged(Color value) => UpdatePreviewColors();

    partial void OnSelectedPreviewSceneChanged(string? value) => LoadPreviewScene();

    private void UpdatePreviewColors()
    {
        var colors = new Dictionary<string, Color>();
        foreach (PaletteColor paletteColor in SelectedPalette?.Colors ?? [])
        {
            colors[paletteColor.Name] = ((ImmutableSolidColorBrush)ToRow(paletteColor).Swatch).Color;
        }

        if (SelectedPaletteColor is { } selected)
        {
            colors[selected.Name] = EditColor;
        }

        PreviewColors = colors;
    }

    private void LoadPreviewScene()
    {
        if (_session.Draft is not { } draft || SelectedPreviewScene is not { } scenePath)
        {
            PreviewRoot = null;
            return;
        }

        try
        {
            // usages name scenes relative to the theme root ("resources/prefab/bg.scn"), the editor from resources
            PreviewRoot = _sceneEditor.Load(draft.DraftPath, _session.BaseUiPath, scenePath["resources".Length..]).Root;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            PreviewRoot = null;
        }
    }

    partial void OnSelectedPaletteChanged(ThemePalette? value)
    {
        UpdatePreviewColors();
        var previous = SelectedPaletteColor?.Name;
        var used = _allColorUsages.Select(u => u.ColorName).ToHashSet();
        // the Wallpapers tab edits the background of the NES and Famicom UIs
        PaletteColors = value?.Colors
            .Where(c => ShowUnusedColors || used.Contains(c.Name))
            .Where(c => !_pixelBackground.HasPixelBackground || !PixelBackgroundViewModel.IsBackgroundColor(c.Name))
            .Select(c => ToRow(c) with { IsModified = _modifiedNames.Contains(c.Name) })
            .ToList() ?? [];
        SelectedPaletteColor = PaletteColors.FirstOrDefault(row => row.Name == previous)
            ?? (PaletteColors.Count > 0 ? PaletteColors[0] : null);
    }

    partial void OnSelectedPaletteColorChanged(PaletteColorRow? value)
    {
        OnPropertyChanged(nameof(CanResetColor));
        ResetColorCommand.NotifyCanExecuteChanged();
        if (value is not null)
        {
            EditColor = ((ImmutableSolidColorBrush)value.Swatch).Color;
        }

        ColorUsages = value is null ? [] : _allColorUsages.Where(u => u.ColorName == value.Name).ToList();
        UpdatePreviewColors();
        string? previousScene = SelectedPreviewScene;
        PreviewScenes = ColorUsages.Select(usage => usage.ScenePath).Where(path => path.EndsWith(".scn", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        SelectedPreviewScene = previousScene is not null && PreviewScenes.Contains(previousScene) ? previousScene : PreviewScenes.FirstOrDefault();
        ColorUsageSummary = value is null ? ""
            : ColorUsages.Count == 0 ? "Not used by this UI's scenes."
            : $"Used by {ColorUsages.Count} scene components:";
    }

    [RelayCommand]
    private async Task ApplyColorAsync()
    {
        if (_session.Draft is not { } draft || SelectedPaletteColor is not { } row)
        {
            return;
        }

        string name = row.Name;
        var color = PaletteColor.FromBytes(name, EditColor.R, EditColor.G, EditColor.B, EditColor.A);
        List<string> targets = [];

        if (await _errorHandler.TryRunAsync($"Cannot change {name}", () =>
        {
            // a theme has no region, so a colour is written to every palette that carries it
            targets = _paletteEditor.GetPalettes(draft.DraftPath, _session.BaseUiPath)
                .Where(p => p.Colors.Any(c => c.Name == name))
                .Select(p => p.RelativePath)
                .ToList();
            _paletteEditor.SetColor(draft.DraftPath, _session.BaseUiPath, color, targets);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"{PaletteColorNames.Title(name)}: {ToRow(color).Hex} in {string.Join(", ", targets.Select(path => PaletteColorNames.Region(Path.GetFileNameWithoutExtension(path)["colors_".Length..])))}.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetColor))]
    private Task ResetColorAsync() => RestoreAsync(SelectedPaletteColor is { } row ? [row.Name] : [], SelectedPaletteColor?.Title);

    [RelayCommand(CanExecute = nameof(CanResetAllColors))]
    private Task ResetAllColorsAsync() => RestoreAsync(null, "Every colour");

    private async Task RestoreAsync(IEnumerable<string>? names, string? what)
    {
        if (_session.Draft is not { } draft)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync("Cannot restore the colours", () =>
        {
            _paletteEditor.RestoreDefaults(draft.DraftPath, _session.BaseUiPath, names);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"{what} back to the Base's {(names is null ? "colours" : "value")}.";
        }
    }

    private void Reload()
    {
        if (_session.Draft is null)
        {
            Palettes = [];
            SelectedPalette = null;
            return;
        }

        string? previous = SelectedPalette?.RelativePath;
        PreviewImages = _images.Images;
        _allColorUsages = _paletteEditor.GetUsages(_session.Draft.DraftPath, _session.BaseUiPath);
        UpdateModifiedNames();
        Palettes = _paletteEditor.GetPalettes(_session.Draft.DraftPath, _session.BaseUiPath);
        // one list is shown, with the values of the Base's own region; a change is written to every region
        string? ownRegion = _session.BaseDefinition is { } definition ? ConsoleFamilies.PaletteRegion(definition) : null;
        SelectedPalette = Palettes.FirstOrDefault(p => p.Region == ownRegion)
            ?? Palettes.FirstOrDefault(p => p.RelativePath.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ?? (Palettes.Count > 0 ? Palettes[0] : null);
        RegionsText = Palettes.Count == 0 ? "" : "A change applies to " + string.Join(", ", Palettes.Select(p => PaletteColorNames.Region(p.Region))) + ".";
        LoadPreviewScene();
    }

    // the colours some region has at a value other than the Base's
    private void UpdateModifiedNames()
    {
        Dictionary<(string Path, string Name), PaletteColor> defaults = _paletteEditor.GetDefaultPalettes(_session.Draft!.DraftPath, _session.BaseUiPath)
            .SelectMany(palette => palette.Colors.Select(color => (Key: (palette.RelativePath, color.Name), Color: color)))
            .ToDictionary(entry => entry.Key, entry => entry.Color);
        _modifiedNames = _paletteEditor.GetPalettes(_session.Draft.DraftPath, _session.BaseUiPath)
            .SelectMany(palette => palette.Colors.Select(color => (palette.RelativePath, color.Name, color)))
            .Where(entry => defaults.TryGetValue((entry.RelativePath, entry.Name), out PaletteColor baseColor) && baseColor != entry.color)
            .Select(entry => entry.Name)
            .ToHashSet();
        OnPropertyChanged(nameof(CanResetAllColors));
        ResetAllColorsCommand.NotifyCanExecuteChanged();
    }

    private static PaletteColorRow ToRow(PaletteColor color)
    {
        byte r = PaletteColor.ChannelToByte(color.Red);
        byte g = PaletteColor.ChannelToByte(color.Green);
        byte b = PaletteColor.ChannelToByte(color.Blue);
        byte a = PaletteColor.ChannelToByte(color.Alpha);
        return new PaletteColorRow(color.Name, new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b)), ToHex(r, g, b, a), color);
    }

    private static string ToHex(byte r, byte g, byte b, byte a) =>
        a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
}
