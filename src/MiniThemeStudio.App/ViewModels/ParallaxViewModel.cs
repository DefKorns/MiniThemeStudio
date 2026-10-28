using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Scenes;

namespace MiniThemeStudio.App.ViewModels;

public sealed record ParallaxLayerRow(ParallaxLayer Layer)
{
    public string Name => Layer.Name;

    public string Detail => string.Create(CultureInfo.InvariantCulture, $"layer {Layer.ZIndex} · {Layer.PixelsPerSecond:0} px/s");
}

// The scrolling layers of the background, in the Wallpapers tab: their speed, and new layers from a PNG.
public sealed partial class ParallaxViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly IParallaxEditor _parallaxEditor;
    private readonly IFileDialogService _fileDialogs;
    private readonly IDialogService _dialogs;
    private readonly IErrorHandler _errorHandler;

    public ParallaxViewModel(ThemeSession session, IParallaxEditor parallaxEditor, IFileDialogService fileDialogs, IDialogService dialogs, IErrorHandler errorHandler)
    {
        _session = session;
        _parallaxEditor = parallaxEditor;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackgrounds))]
    private IReadOnlyList<ParallaxBackground> backgrounds = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CycleText))]
    [NotifyCanExecuteChangedFor(nameof(AddLayerCommand))]
    private ParallaxBackground? selectedBackground;

    [ObservableProperty]
    private IReadOnlyList<ParallaxLayerRow> layers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    [NotifyCanExecuteChangedFor(nameof(ApplySpeedCommand), nameof(RemoveLayerCommand))]
    private ParallaxLayerRow? selectedLayer;

    // how many times the selected layer slides by its own width every cycle
    [ObservableProperty]
    private decimal? layerLoops;

    [ObservableProperty]
    private string newLayerName = "layer";

    [ObservableProperty]
    private decimal? newLayerY = 0;

    [ObservableProperty]
    private decimal? newLayerZIndex = -1;

    [ObservableProperty]
    private decimal? newLayerScale = 3;

    [ObservableProperty]
    private decimal? newLayerLoops = 1;

    public bool HasBackgrounds => Backgrounds.Count > 0;

    public bool HasSelectedLayer => SelectedLayer is not null;

    public string CycleText => SelectedBackground is { } background
        ? string.Create(CultureInfo.InvariantCulture, $"Every layer slides a whole number of times its own width in each {background.CycleSeconds:0.##} s cycle; wider and faster layers look nearer.")
        : "";

    private bool CanAddLayer => SelectedBackground is not null;

    [RelayCommand(CanExecute = nameof(CanAddLayer))]
    private async Task AddLayerAsync()
    {
        if (_session.Draft is not { } draft
            || SelectedBackground is not { } background
            || await _fileDialogs.OpenFileAsync("Select the PNG of the new layer", new FileType("PNG image", "*.png")) is not { } imagePath)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewLayerName) ? Path.GetFileNameWithoutExtension(imagePath) : NewLayerName.Trim();
        var layer = new NewParallaxLayer(name, imagePath, (double)(NewLayerY ?? 0), (int)(NewLayerZIndex ?? 0), (int)(NewLayerScale ?? 3), (int)(NewLayerLoops ?? 1));
        string? nodePath = null;
        if (await _errorHandler.TryRunAsync($"Cannot add the layer {name}", () =>
        {
            nodePath = _parallaxEditor.AddLayer(draft.DraftPath, _session.BaseUiPath, background, layer);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            SelectedLayer = Layers.FirstOrDefault(row => row.Layer.NodePath == nodePath) ?? SelectedLayer;
            _session.Status = $"Added the scrolling layer {SelectedLayer?.Name ?? name} to {background.AnimatorLabel}.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private async Task ApplySpeedAsync()
    {
        if (_session.Draft is not { } draft || SelectedBackground is not { } background || SelectedLayer is not { } row || LayerLoops is not { } loops)
        {
            return;
        }

        var name = row.Name;
        if (await _errorHandler.TryRunAsync($"Cannot change the speed of {name}", () =>
        {
            _parallaxEditor.SetSpeed(draft.DraftPath, _session.BaseUiPath, background, name, (int)loops);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = string.Create(CultureInfo.InvariantCulture, $"{name} now scrolls at {SelectedLayer?.Layer.PixelsPerSecond ?? 0:0} px/s.");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private async Task RemoveLayerAsync()
    {
        if (_session.Draft is not { } draft || SelectedBackground is not { } background || SelectedLayer is not { } row)
        {
            return;
        }

        if (!await _dialogs.ShowConfirmationAsync(
            "Remove layer",
            $"Remove {row.Name} from {background.AnimatorLabel}? Its node and its track in {Path.GetFileName(background.AnimationResourcePath)} are removed; its image stays in the theme.",
            acceptButtonText: "Remove",
            isDestructive: true))
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot remove {row.Name}", () =>
        {
            _parallaxEditor.RemoveLayer(draft.DraftPath, _session.BaseUiPath, background, row.Name);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Removed the scrolling layer {row.Name}.";
        }
    }

    partial void OnSelectedBackgroundChanged(ParallaxBackground? value)
    {
        var previous = SelectedLayer?.Name;
        Layers = value?.Layers.Select(layer => new ParallaxLayerRow(layer)).ToList() ?? [];
        SelectedLayer = Layers.FirstOrDefault(row => row.Name == previous) ?? (Layers.Count > 0 ? Layers[0] : null);
    }

    partial void OnSelectedLayerChanged(ParallaxLayerRow? value)
    {
        LayerLoops = value is null ? null : (decimal)value.Layer.LoopsPerCycle;
    }

    private void Reload()
    {
        var previous = SelectedBackground?.AnimationResourcePath;
        var previousLayer = SelectedLayer?.Name;
        Backgrounds = _session.Draft is { } draft ? _parallaxEditor.GetBackgrounds(draft.DraftPath, _session.BaseUiPath) : [];

        // the console's demo background, with the most layers, comes first
        SelectedBackground = null;
        SelectedBackground = Backgrounds.FirstOrDefault(background => background.AnimationResourcePath == previous)
            ?? Backgrounds.MaxBy(background => background.Layers.Count);
        SelectedLayer = Layers.FirstOrDefault(row => row.Name == previousLayer) ?? SelectedLayer;
    }
}
