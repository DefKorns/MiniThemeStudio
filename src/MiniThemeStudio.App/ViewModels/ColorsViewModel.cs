using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Controls;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// IsModified: some region's value of the colour is not the Base's.
// Samples are the words the console draws with the colour ("Menu · Sort · Start Game"), Group the screen they are on.
public sealed record PaletteColorRow(
    string Name,
    IBrush Swatch,
    string Hex,
    PaletteColor Color,
    bool IsModified = false,
    string Samples = "",
    string Group = "",
    bool StartsGroup = false)
{
    public string Title => PaletteColorNames.Title(Name);
}

// The Colors tab: the colours of the Lua palettes, grouped by the screen they colour, with the words they colour.
public sealed partial class ColorsViewModel : ViewModelBase, IDisposable
{
    private const int MaxSamples = 3;
    private const int MaxSampleLength = 28;

    // the screens, in the order the list shows them; a colour is on the screen its name starts with
    private static readonly (string Prefix, string Group)[] Screens =
    [
        ("font_home", "Home"),
        ("font_option", "Options"),
        ("font_dialog", "Dialogs"),
        ("font_resume", "Suspend points"),
        ("bg_", "Background"),
    ];

    private const string OtherScreen = "Other";

    // the colours of paragraphs: their words are shown as a line of text, not on the screen they are laid out on
    private static readonly string[] ParagraphColors = ["font_option_text"];

    // the screens of the options are black behind everything
    private static readonly Color OptionsBackground = Color.FromRgb(0, 0, 0);

    // how many times the console enlarges the picture of its wall
    private const double WallScale = 2;

    private static readonly IBrush LightSample = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static readonly IBrush DarkSample = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14));

    private readonly ThemeSession _session;
    private readonly IThemePaletteEditor _paletteEditor;
    private readonly IErrorHandler _errorHandler;
    private readonly PixelBackgroundViewModel _pixelBackground;
    private readonly SceneImageLibrary _images;
    private readonly ISceneEditor _sceneEditor;
    private readonly Dictionary<string, SceneNode?> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _modifiedNames = [];
    private Bitmap? _wall;
    private Color? _homeBackground;
    private IReadOnlyList<PaletteColorUsage> _allColorUsages = [];

    public ColorsViewModel(ThemeSession session, IThemePaletteEditor paletteEditor, IErrorHandler errorHandler, PixelBackgroundViewModel pixelBackground, SceneImageLibrary images, ISceneEditor sceneEditor)
    {
        _images = images;
        _sceneEditor = sceneEditor;
        _session = session;
        _paletteEditor = paletteEditor;
        _errorHandler = errorHandler;
        _pixelBackground = pixelBackground;
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
    [NotifyPropertyChangedFor(nameof(EditBrush))]
    [NotifyPropertyChangedFor(nameof(SampleBackground))]
    private Color editColor;

    // a palette carries every colour of the engine, many of them for other consoles' UIs
    [ObservableProperty]
    private bool showUnusedColors;

    // the selected colour being edited, and the words the console draws with it, to see how they read
    public IBrush EditBrush => new ImmutableSolidColorBrush(EditColor);

    // where the sample is drawn: the Base does not say what a text sits on, so it is a plain background that
    // the colour reads on, light for a dark colour and dark for a light one
    public IBrush SampleBackground =>
        _homeBackground is { } home ? new ImmutableSolidColorBrush(home) : Luminance(EditColor) < 0.5 ? LightSample : DarkSample;

    public string SampleText => SampleTextOf(SelectedPaletteColor?.Name) ?? SelectedPaletteColor?.Title ?? "";

    // the group of nodes the selected colour's text belongs to (the text with the balloon or bar it sits on), drawn
    // with the palette, the colour being edited live, and the words the console shows
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSampleScene))]
    private SceneNode? sampleRoot;

    [ObservableProperty]
    private SceneImageSource sampleImages = SceneImageSource.Empty;

    [ObservableProperty]
    private IReadOnlyDictionary<string, Color> sampleColors = new Dictionary<string, Color>();

    [ObservableProperty]
    private IReadOnlyDictionary<string, string> sampleStrings = new Dictionary<string, string>();

    public bool HasSampleScene => SampleRoot is not null;

    // behind the group: the wall of the SNES home screen, shown as big as the console draws it
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSampleBackdrop))]
    private Bitmap? sampleBackdrop;

    public bool HasSampleBackdrop => SampleBackdrop is not null;

    public double SampleBackdropWidth => (SampleBackdrop?.PixelSize.Width ?? 0) * WallScale;

    public double SampleBackdropHeight => (SampleBackdrop?.PixelSize.Height ?? 0) * WallScale;

    partial void OnEditColorChanged(Color value) => UpdateSampleColors();

    // the screens that draw the selected colour: "Used on: Home, Options: Display"
    public string UsedOn => UsedOnOf(SelectedPaletteColor?.Name);

    public bool CanResetColor => SelectedPaletteColor is { IsModified: true };

    public bool CanResetAllColors => _modifiedNames.Count > 0;

    partial void OnShowUnusedColorsChanged(bool value) => OnSelectedPaletteChanged(SelectedPalette);

    partial void OnSelectedPaletteChanged(ThemePalette? value)
    {
        UpdateSampleColors();
        var previous = SelectedPaletteColor?.Name;
        var used = _allColorUsages.Select(u => u.ColorName).ToHashSet();
        // the Wallpapers tab edits the background of the NES and Famicom UIs
        List<PaletteColorRow> rows = value?.Colors
            .Where(c => ShowUnusedColors || used.Contains(c.Name))
            .Where(c => !_pixelBackground.HasPixelBackground || !PixelBackgroundViewModel.IsBackgroundColor(c.Name))
            .Select(c => ToRow(c) with
            {
                IsModified = _modifiedNames.Contains(c.Name),
                Samples = SamplesOf(c.Name),
                Group = ScreenOf(c.Name),
            })
            .OrderBy(row => ScreenOrder(row.Group))
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList() ?? [];
        PaletteColors = rows
            .Select((row, index) => row with { StartsGroup = index == 0 || rows[index - 1].Group != row.Group })
            .ToList();
        SelectedPaletteColor = PaletteColors.FirstOrDefault(row => row.Name == previous)
            ?? (PaletteColors.Count > 0 ? PaletteColors[0] : null);
    }

    partial void OnSelectedPaletteColorChanged(PaletteColorRow? value)
    {
        OnPropertyChanged(nameof(CanResetColor));
        OnPropertyChanged(nameof(SampleText));
        OnPropertyChanged(nameof(UsedOn));
        UpdateSample();
        ResetColorCommand.NotifyCanExecuteChanged();
        if (value is not null)
        {
            EditColor = ((ImmutableSolidColorBrush)value.Swatch).Color;
        }
    }

    public void Dispose() => _wall?.Dispose();

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
            SampleBackdrop = null;
            _wall?.Dispose();
            _wall = null;
            return;
        }

        string? previous = SelectedPalette?.RelativePath;
        SampleImages = _images.Images;
        _scenes.Clear();
        _allColorUsages = _paletteEditor.GetUsages(_session.Draft.DraftPath, _session.BaseUiPath);
        SampleStrings = _allColorUsages
            .Where(usage => usage.TextKey is { Length: > 1 } key && key[0] == '@' && usage.Text is not null)
            .GroupBy(usage => usage.TextKey![1..])
            .ToDictionary(group => group.Key, group => group.First().Text!);
        UpdateModifiedNames();
        Palettes = _paletteEditor.GetPalettes(_session.Draft.DraftPath, _session.BaseUiPath);
        // one list is shown, with the values of the Base's own region; a change is written to every region
        string? ownRegion = _session.BaseDefinition is { } definition ? ConsoleFamilies.PaletteRegion(definition) : null;
        SelectedPalette = Palettes.FirstOrDefault(p => p.Region == ownRegion)
            ?? Palettes.FirstOrDefault(p => p.RelativePath.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ?? (Palettes.Count > 0 ? Palettes[0] : null);
        RegionsText = Palettes.Count == 0 ? "" : "A change applies to " + string.Join(", ", Palettes.Select(p => PaletteColorNames.Region(p.Region))) + ".";
        // the images were loaded again, and the ones the backdrop was cut from are gone
        LoadWall();
        UpdateSample();
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

    // what the text is drawn on: its group of nodes, over the wall of the SNES home screen or the background colour
    // of the NES and the Famicom for the texts of the home screen
    private void UpdateSample()
    {
        string? name = SelectedPaletteColor?.Name;
        SampleRoot = name is not null && ParagraphColors.Contains(name) ? null : GroupOf(name);
        SampleBackdrop = null;
        _homeBackground = name is not null && name.StartsWith("font_option", StringComparison.Ordinal) ? OptionsBackground : null;
        if (name is not null && name.StartsWith("font_home", StringComparison.Ordinal))
        {
            PaletteColor? background = SelectedPalette?.Colors.Where(color => color.Name == "bg_base").Select(color => (PaletteColor?)color).FirstOrDefault();
            if (_pixelBackground.HasPixelBackground && background is { } colour)
            {
                _homeBackground = ((ImmutableSolidColorBrush)ToRow(colour).Swatch).Color;
            }
            else
            {
                SampleBackdrop = _wall;
            }
        }

        OnPropertyChanged(nameof(SampleBackdropWidth));
        OnPropertyChanged(nameof(SampleBackdropHeight));
        OnPropertyChanged(nameof(SampleBackground));
    }

    private void UpdateSampleColors()
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

        SampleColors = colors;
    }

    // the node that holds the text of the colour with what is beside it: the parent of its first label that has
    // words (many are empty in the scene and filled in by the console's scripts)
    private SceneNode? GroupOf(string? colorName)
    {
        PaletteColorUsage? usage = colorName is null ? null : _allColorUsages.FirstOrDefault(candidate =>
            candidate.ColorName == colorName
            && !string.IsNullOrWhiteSpace(candidate.Text)
            && candidate.ScenePath.EndsWith(".scn", StringComparison.OrdinalIgnoreCase));
        if (usage is null || LoadScene(usage.ScenePath) is not { } root)
        {
            return null;
        }

        // "/Main/homemenu/sort_order/sort_order_label": from the root, through the names, to the parent of the label
        SceneNode node = root;
        foreach (string segment in usage.NodePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).SkipLast(1))
        {
            if (node.Children.FirstOrDefault(child => child.Name == segment) is not { } next)
            {
                return null;
            }

            node = next;
        }

        return node;
    }

    // usages name scenes relative to the theme root ("resources/prefab/bg.scn"), the editor from resources
    private SceneNode? LoadScene(string scenePath)
    {
        if (_scenes.TryGetValue(scenePath, out SceneNode? loaded))
        {
            return loaded;
        }

        try
        {
            loaded = _sceneEditor.Load(_session.Draft!.DraftPath, _session.BaseUiPath, scenePath["resources".Length..]).Root;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            loaded = null;
        }

        _scenes[scenePath] = loaded;
        return loaded;
    }

    // the picture behind the home screen of the SNES, from the theme or else the Base
    private void LoadWall()
    {
        _wall?.Dispose();
        _wall = null;
        const string wall = "resources/layout/asset/sprite/wallpaper/wall.png";
        string? path = new[] { _session.Draft?.DraftPath, _session.BaseUiPath }
            .Where(root => root is not null)
            .Select(root => Path.Combine(root!, wall))
            .FirstOrDefault(File.Exists);
        if (path is null)
        {
            return;
        }

        try
        {
            _wall = new Bitmap(path);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
        }
    }

    private string UsedOnOf(string? colorName)
    {
        if (colorName is null)
        {
            return "";
        }

        List<PaletteColorUsage> usages = _allColorUsages.Where(usage => usage.ColorName == colorName).ToList();
        List<string> screens = usages
            .Where(usage => usage.ScenePath.EndsWith(".scn", StringComparison.OrdinalIgnoreCase))
            .Select(usage => BranchOf(usage.NodePath))
            .OfType<string>()
            .Select(PaletteColorNames.Screen)
            .Distinct()
            .ToList();
        if (screens.Count > 0)
        {
            return "Used on: " + string.Join(", ", screens);
        }

        return usages.Count > 0 ? "Drawn by the console's scripts." : "Not used by this UI.";
    }

    // "/Main/option_display/title" is on the screen "option_display"
    private static string? BranchOf(string nodePath)
    {
        string[] names = nodePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return names.Length > 1 ? names[1] : null;
    }

    // the words a colour draws, each once, the shortest few: "Menu · Sort · Start Game"
    private IEnumerable<string> TextsOf(string colorName) =>
        _allColorUsages
            .Where(usage => usage.ColorName == colorName && !string.IsNullOrWhiteSpace(usage.Text))
            .Select(usage => string.Join(' ', usage.Text!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Distinct(StringComparer.Ordinal);

    private string SamplesOf(string colorName) =>
        string.Join(" · ", TextsOf(colorName)
            .OrderBy(text => text.Length)
            .Take(MaxSamples)
            .Select(text => text.Length > MaxSampleLength ? text[..(MaxSampleLength - 1)] + "…" : text));

    private string? SampleTextOf(string? colorName) =>
        colorName is null ? null : TextsOf(colorName).OrderBy(text => text.Length).FirstOrDefault();

    // 0 for black to 1 for white, as the eye weighs the channels
    private static double Luminance(Color color) =>
        (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;

    private static string ScreenOf(string colorName) =>
        Screens.FirstOrDefault(screen => colorName.StartsWith(screen.Prefix, StringComparison.Ordinal)).Group ?? OtherScreen;

    private static int ScreenOrder(string group)
    {
        int index = Array.FindIndex(Screens, screen => screen.Group == group);
        return index < 0 ? Screens.Length : index;
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
