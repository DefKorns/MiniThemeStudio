using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public sealed record PaletteColorRow(string Name, IBrush Swatch, string Hex, PaletteColor Color);

public partial class MainWindowViewModel
{
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

    public bool ApplyPaletteColor()
    {
        if (_draft is null || SelectedPalette is null || SelectedPaletteColor is null)
        {
            Status = "Select a colour first.";
            return false;
        }

        var name = SelectedPaletteColor.Name;
        var color = PaletteColor.FromBytes(name, EditColor.R, EditColor.G, EditColor.B, EditColor.A);
        var targets = ApplyColorToAllRegions
            ? Palettes.Where(p => p.Colors.Any(c => c.Name == name)).Select(p => p.RelativePath).ToList()
            : [SelectedPalette.RelativePath];

        try
        {
            _paletteEditor.SetColor(_draft.DraftPath, _currentBaseUiPath, color, targets);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }

        ReloadPalettes();
        MarkChanged();
        Status = $"{name}: {ToRow(color).Hex} in {string.Join(", ", targets.Select(t => System.IO.Path.GetFileNameWithoutExtension(t)))}.";
        return true;
    }

    private void ReloadPalettes()
    {
        if (_draft is null)
        {
            Palettes = [];
            SelectedPalette = null;
            return;
        }

        var previous = SelectedPalette?.RelativePath;
        _allColorUsages = _paletteEditor.GetUsages(_draft.DraftPath, _currentBaseUiPath);
        Palettes = _paletteEditor.GetPalettes(_draft.DraftPath, _currentBaseUiPath);
        SelectedPalette = Palettes.FirstOrDefault(p => p.RelativePath.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ?? (Palettes.Count > 0 ? Palettes[0] : null);
    }

    private static PaletteColorRow ToRow(PaletteColor color)
    {
        var r = PaletteColor.ChannelToByte(color.Red);
        var g = PaletteColor.ChannelToByte(color.Green);
        var b = PaletteColor.ChannelToByte(color.Blue);
        var a = PaletteColor.ChannelToByte(color.Alpha);
        return new PaletteColorRow(color.Name, new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b)), ToHex(r, g, b, a), color);
    }

    private static string ToHex(byte r, byte g, byte b, byte a) =>
        a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
}
