using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public sealed record PaletteColorRow(string Name, IBrush Swatch, string Hex, PaletteColor Color);

// The Colors tab: the Lua colour palettes of each region and where the scenes use each colour.
public sealed partial class ColorsViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly IThemePaletteEditor _paletteEditor;
    private readonly IErrorHandler _errorHandler;

    public ColorsViewModel(ThemeSession session, IThemePaletteEditor paletteEditor, IErrorHandler errorHandler)
    {
        _session = session;
        _paletteEditor = paletteEditor;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    [ObservableProperty]
    private IReadOnlyList<ThemePalette> palettes = [];

    [ObservableProperty]
    private ThemePalette? selectedPalette;

    [ObservableProperty]
    private IReadOnlyList<PaletteColorRow> paletteColors = [];

    [ObservableProperty]
    private PaletteColorRow? selectedPaletteColor;

    [ObservableProperty]
    private Color editColor;

    // a theme with one palette shows its colours on every region only if all of them carry it
    [ObservableProperty]
    private bool applyColorToAllRegions = true;

    private IReadOnlyList<PaletteColorUsage> _allColorUsages = [];

    // a palette carries every colour of the engine, many of them for other consoles' UIs
    [ObservableProperty]
    private bool showUnusedColors;

    partial void OnShowUnusedColorsChanged(bool value) => OnSelectedPaletteChanged(SelectedPalette);

    [ObservableProperty]
    private IReadOnlyList<PaletteColorUsage> colorUsages = [];

    [ObservableProperty]
    private string colorUsageSummary = "";

    partial void OnSelectedPaletteChanged(ThemePalette? value)
    {
        var previous = SelectedPaletteColor?.Name;
        var used = _allColorUsages.Select(u => u.ColorName).ToHashSet();
        PaletteColors = value?.Colors.Where(c => ShowUnusedColors || used.Contains(c.Name)).Select(ToRow).ToList() ?? [];
        SelectedPaletteColor = PaletteColors.FirstOrDefault(row => row.Name == previous)
            ?? (PaletteColors.Count > 0 ? PaletteColors[0] : null);
    }

    partial void OnSelectedPaletteColorChanged(PaletteColorRow? value)
    {
        if (value is not null)
        {
            EditColor = ((ImmutableSolidColorBrush)value.Swatch).Color;
        }

        ColorUsages = value is null ? [] : _allColorUsages.Where(u => u.ColorName == value.Name).ToList();
        ColorUsageSummary = value is null ? ""
            : ColorUsages.Count == 0 ? "Not used by this UI's scenes."
            : $"Used by {ColorUsages.Count} scene components:";
    }

    [RelayCommand]
    private async Task ApplyColorAsync()
    {
        if (_session.Draft is not { } draft || SelectedPalette is not { } palette || SelectedPaletteColor is not { } row)
        {
            return;
        }

        string name = row.Name;
        var color = PaletteColor.FromBytes(name, EditColor.R, EditColor.G, EditColor.B, EditColor.A);
        List<string> targets = ApplyColorToAllRegions
            ? Palettes.Where(p => p.Colors.Any(c => c.Name == name)).Select(p => p.RelativePath).ToList()
            : [palette.RelativePath];

        if (await _errorHandler.TryRunAsync($"Cannot change {name}", () =>
        {
            _paletteEditor.SetColor(draft.DraftPath, _session.BaseUiPath, color, targets);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"{name}: {ToRow(color).Hex} in {string.Join(", ", targets.Select(Path.GetFileNameWithoutExtension))}.";
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
        _allColorUsages = _paletteEditor.GetUsages(_session.Draft.DraftPath, _session.BaseUiPath);
        Palettes = _paletteEditor.GetPalettes(_session.Draft.DraftPath, _session.BaseUiPath);
        SelectedPalette = Palettes.FirstOrDefault(p => p.RelativePath.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ?? (Palettes.Count > 0 ? Palettes[0] : null);
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
