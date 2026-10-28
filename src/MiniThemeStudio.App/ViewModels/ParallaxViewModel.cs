using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Controls;
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

// The scrolling layers of the background, in the Wallpapers tab: their speed, and new layers from a PNG, with the
// background playing in a preview as the console shows it (and the speed being tried before it is applied).
public sealed partial class ParallaxViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly IParallaxEditor _parallaxEditor;
    private readonly ISceneEditor _sceneEditor;
    private readonly SceneImageLibrary _images;
    private readonly IFileDialogService _fileDialogs;
    private readonly IDialogService _dialogs;
    private readonly IErrorHandler _errorHandler;

    private SceneDocument? _scene;
    private SceneAnimationBinding? _animation;

    public ParallaxViewModel(
        ThemeSession session,
        IParallaxEditor parallaxEditor,
        ISceneEditor sceneEditor,
        SceneImageLibrary images,
        IFileDialogService fileDialogs,
        IDialogService dialogs,
        IErrorHandler errorHandler)
    {
        _session = session;
        _parallaxEditor = parallaxEditor;
        _sceneEditor = sceneEditor;
        _images = images;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
        Clock.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AnimationClock.Time))
            {
                UpdatePreview();
            }
        };
    }

    public AnimationClock Clock { get; } = new();

    // the background as it moves at Clock.Time, drawn alone (PreviewBranchPath) with the selected layer framed
    [ObservableProperty]
    private SceneNode? previewRoot;

    [ObservableProperty]
    private string? previewBranchPath;

    [ObservableProperty]
    private SceneImageSource images = SceneImageSource.Empty;

    // the node of the selected layer; clicking a layer in the preview selects it
    [ObservableProperty]
    private string? previewSelectedPath;

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

    // how many times the selected layer slides by its own width every cycle; the preview shows it before Apply
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
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

    public string SpeedText => SelectedLayer is { } row && LayerLoops is { } loops
        ? string.Create(CultureInfo.InvariantCulture, $"{(double)loops * row.Layer.TileWidth / Math.Max(0.001, row.Layer.CycleSeconds):0} px/s")
            + ((double)loops == row.Layer.LoopsPerCycle ? "" : " (not applied yet)")
        : "";

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
            $"Remove {row.Name} from {background.AnimatorLabel}? Its node, its track in {Path.GetFileName(background.AnimationResourcePath)} and, when nothing else draws it, its image are removed.",
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
        LoadPreview(value);
        SelectedLayer = Layers.FirstOrDefault(row => row.Name == previous) ?? (Layers.Count > 0 ? Layers[0] : null);
    }

    partial void OnSelectedLayerChanged(ParallaxLayerRow? value)
    {
        LayerLoops = value is null ? null : (decimal)value.Layer.LoopsPerCycle;
        PreviewSelectedPath = value?.Layer.NodePath;
        OnPropertyChanged(nameof(SpeedText));
        UpdatePreview();
    }

    partial void OnLayerLoopsChanged(decimal? value) => UpdatePreview();

    partial void OnPreviewSelectedPathChanged(string? value)
    {
        if (value is not null && Layers.FirstOrDefault(row => row.Layer.NodePath == value) is { } row && row != SelectedLayer)
        {
            SelectedLayer = row;
        }
    }

    // the scene and the animation of the background, played from the start
    private void LoadPreview(ParallaxBackground? background)
    {
        _scene = null;
        _animation = null;
        if (_session.Draft is { } draft && background is not null)
        {
            try
            {
                _scene = _sceneEditor.Load(draft.DraftPath, _session.BaseUiPath, background.SceneResourcePath);
                _animation = _sceneEditor.GetAnimations(draft.DraftPath, _session.BaseUiPath, _scene)
                    .FirstOrDefault(binding => binding.Clip.ResourcePath == background.AnimationResourcePath && binding.AnimatorPath == background.AnimatorPath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                _scene = null;
            }
        }

        PreviewBranchPath = background?.AnimatorPath.Split('/')[0];
        if (_animation is { } animation)
        {
            Clock.Reset(animation.Clip.Duration);
            Clock.TogglePlayCommand.Execute(null);
        }
        else
        {
            Clock.Reset(0);
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_scene is not { } scene || _animation is not { } animation)
        {
            PreviewRoot = _scene?.Root;
            return;
        }

        // the speed being tried replaces the saved one of the selected layer, as Apply would write it
        if (SelectedLayer is { } row && LayerLoops is { } loops && (double)loops != row.Layer.LoopsPerCycle)
        {
            var cycle = animation.Clip.Duration;
            var slope = cycle > 0 ? (double)loops / cycle : 0;
            var tried = new CurveKey[]
            {
                new(0, CurveInterpolation.Hermite, 0, (double)loops, slope, slope),
                new(cycle, CurveInterpolation.Constant, (double)loops),
            };
            var tracks = animation.Clip.Tracks
                .Select(track => track.PropertyId == "TextureOffsetX" && track.NodePath is [var name] && name == row.Name ? track with { Keys = tried } : track)
                .ToList();
            animation = animation with { Clip = animation.Clip with { Tracks = tracks } };
        }

        PreviewRoot = SceneAnimationEvaluator.Apply(scene.Root, animation, Clock.Time);
    }

    private void Reload()
    {
        Images = _images.Images;
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
