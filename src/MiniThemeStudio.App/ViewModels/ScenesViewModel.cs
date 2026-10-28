using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Controls;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public sealed record SceneTreeRow(SceneNode Node, int Depth)
{
    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    public string Name => Node.Name.Length > 0 ? Node.Name : "(unnamed)";

    public string Detail => Node.Visual?.Kind switch
    {
        SceneVisualKind.Texture => "image",
        SceneVisualKind.Sprite => "sprite",
        SceneVisualKind.Label => "text",
        _ => Node.Children.Count > 0 ? $"{Node.Children.Count}" : "",
    };

    public double Opacity => Node.Visible && Node.Enabled ? 1 : 0.45;
}

public sealed record SceneTextureChoice(string TextureId, string Name, string RelativePath);

// an atlas sprite a scene node draws, to be edited in the Sprites tab
public sealed record SceneSpriteRequest(string TextureId, string SpriteName, string? ImagePath);

// The Scenes tab: browse the nodes of a .scn file, preview them and edit the selected node.
public sealed partial class ScenesViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly ISceneEditor _sceneEditor;
    private readonly IThemeAssetManager _assetManager;
    private readonly ITextureAtlasReader _atlasReader;
    private readonly IFileDialogService _fileDialogs;
    private readonly IDialogService _dialogs;
    private readonly IErrorHandler _errorHandler;
    private IReadOnlyList<SceneTreeRow> _allSceneRows = [];
    private Dictionary<string, string> _textureImagePaths = new(StringComparer.Ordinal);

    public ScenesViewModel(
        ThemeSession session,
        ISceneEditor sceneEditor,
        IThemeAssetManager assetManager,
        ITextureAtlasReader atlasReader,
        IFileDialogService fileDialogs,
        IDialogService dialogs,
        IErrorHandler errorHandler)
    {
        _session = session;
        _sceneEditor = sceneEditor;
        _assetManager = assetManager;
        _atlasReader = atlasReader;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    // "Edit sprite in Sprites tab": the window selects the sprite and switches tab
    public event EventHandler<SceneSpriteRequest>? SpriteRequested;

    [ObservableProperty]
    private IReadOnlyList<string> sceneResources = [];

    [ObservableProperty]
    private string? selectedSceneResource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddImageCommand))]
    private SceneDocument? currentScene;

    [ObservableProperty]
    private IReadOnlyList<SceneTreeRow> sceneRows = [];

    [ObservableProperty]
    private string sceneSearchText = "";

    [ObservableProperty]
    private SceneTreeRow? selectedSceneRow;

    [ObservableProperty]
    private string? selectedScenePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSceneNode), nameof(SelectedNodeDrawsImage), nameof(SelectedNodeIsTexture), nameof(SelectedNodeIsSprite), nameof(SelectedSceneNodeSummary))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateNodeCommand), nameof(DeleteNodeCommand))]
    private SceneNode? selectedSceneNode;

    [ObservableProperty]
    private SceneImageSource sceneImages = SceneImageSource.Empty;

    [ObservableProperty]
    private bool isolateSceneBranch = true;

    [ObservableProperty]
    private bool showHiddenSceneNodes;

    [ObservableProperty]
    private string? previewBranchPath;

    [ObservableProperty]
    private IReadOnlyList<SceneTextureChoice> sceneTextureChoices = [];

    // the editable copy of the selected node, written by ApplyNodeChanges
    [ObservableProperty]
    private string sceneNodeName = "";

    [ObservableProperty]
    private bool sceneNodeVisible;

    [ObservableProperty]
    private decimal? sceneNodeX;

    [ObservableProperty]
    private decimal? sceneNodeY;

    [ObservableProperty]
    private decimal? sceneNodeZIndex;

    [ObservableProperty]
    private decimal? sceneNodeWidth;

    [ObservableProperty]
    private decimal? sceneNodeHeight;

    [ObservableProperty]
    private SceneTextureChoice? sceneNodeTexture;

    public bool HasSelectedSceneNode => SelectedSceneNode is not null;

    public bool SelectedNodeDrawsImage => SelectedSceneNode?.Visual is { Kind: SceneVisualKind.Texture or SceneVisualKind.Sprite };

    public bool SelectedNodeIsTexture => SelectedSceneNode?.Visual is { Kind: SceneVisualKind.Texture };

    public bool SelectedNodeIsSprite => SelectedSceneNode?.Visual is { Kind: SceneVisualKind.Sprite };

    public string SelectedSceneNodeSummary => SelectedSceneNode switch
    {
        null => "Select a node in the list or click an image in the preview.",
        { Visual: { Kind: SceneVisualKind.Sprite, SpriteId: var spriteId } } => $"Draws the atlas sprite {SpriteName(spriteId)}.",
        { Visual: { Kind: SceneVisualKind.Label, Text: var text } } => $"Draws the text \"{text}\".",
        { Visual: { Kind: SceneVisualKind.Texture } } => "Draws an image.",
        { Children.Count: > 0 } node => $"Group of {node.Children.Count} node{(node.Children.Count == 1 ? "" : "s")}" + (node.ComponentTypes.Count > 0 ? $" ({node.ComponentsText})." : "."),
        var node => node.ComponentTypes.Count > 0 ? $"Components: {node.ComponentsText}." : "Empty node.",
    };

    private bool HasScene => CurrentScene is not null;

    // the root holds the whole scene: it is never copied or removed
    private bool CanCopyOrDeleteNode => CurrentScene is not null && SelectedSceneNode is { Path.Length: > 0 };

    // new images go inside the selected group, next to the selected image, or at the top of the scene
    private string NewNodeParentPath => SelectedSceneNode switch
    {
        null => "",
        { Visual: null } group => group.Path,
        { Path: var path } => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "",
    };

    // a PNG from disk becomes a texture of the theme and a node that draws it
    [RelayCommand(CanExecute = nameof(HasScene))]
    private async Task AddImageAsync()
    {
        if (_session.Draft is null
            || CurrentScene is not { } scene
            || await _fileDialogs.OpenFileAsync("Select the PNG to add to the scene", new FileType("PNG image", "*.png")) is not { } imagePath)
        {
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        var parentPath = NewNodeParentPath;
        var name = Path.GetFileNameWithoutExtension(imagePath);
        string? nodePath = null;
        if (await _errorHandler.TryRunAsync($"Cannot add {Path.GetFileName(imagePath)}", () =>
        {
            var textureId = _sceneEditor.ImportTexture(draftPath, _session.BaseUiPath, imagePath);
            nodePath = _sceneEditor.AddImageNode(draftPath, _session.BaseUiPath, scene.ResourcePath, parentPath, textureId, name);
            return Task.CompletedTask;
        }))
        {
            ShowEditedNode(nodePath, $"{scene.FileName}: added {name}.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrDeleteNode))]
    private async Task DuplicateNodeAsync()
    {
        if (_session.Draft is null || CurrentScene is not { } scene || SelectedSceneNode is not { } node)
        {
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        string? copyPath = null;
        if (await _errorHandler.TryRunAsync($"Cannot duplicate {node.Name}", () =>
        {
            copyPath = _sceneEditor.DuplicateNode(draftPath, _session.BaseUiPath, scene.ResourcePath, node.Path);
            return Task.CompletedTask;
        }))
        {
            ShowEditedNode(copyPath, $"{scene.FileName}: duplicated {node.Name}.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrDeleteNode))]
    private async Task DeleteNodeAsync()
    {
        if (_session.Draft is null || CurrentScene is not { } scene || SelectedSceneNode is not { } node)
        {
            return;
        }

        var descendants = node.SelfAndDescendants().Count() - 1;
        var message = descendants == 0
            ? $"Delete {node.Name} from {scene.FileName}?"
            : $"Delete {node.Name} and the {descendants} node{(descendants == 1 ? "" : "s")} inside it from {scene.FileName}?";
        if (!await _dialogs.ShowConfirmationAsync("Delete node", message + " Only this theme's copy of the scene changes.", acceptButtonText: "Delete", isDestructive: true))
        {
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        var parentPath = node.Path.LastIndexOf('/') is var slash and >= 0 ? node.Path[..slash] : "";
        if (await _errorHandler.TryRunAsync($"Cannot delete {node.Name}", () =>
        {
            _sceneEditor.DeleteNode(draftPath, _session.BaseUiPath, scene.ResourcePath, node.Path);
            return Task.CompletedTask;
        }))
        {
            ShowEditedNode(parentPath, $"{scene.FileName}: deleted {node.Name}.");
        }
    }

    // a node dragged in the preview
    [RelayCommand]
    private async Task MoveNodeAsync(SceneNodeMove? move)
    {
        if (_session.Draft is null || CurrentScene is not { } scene || move is null || scene.Find(move.Path) is not { } node)
        {
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        if (await _errorHandler.TryRunAsync($"Cannot move {node.Name}", () =>
        {
            _sceneEditor.EditNode(draftPath, _session.BaseUiPath, scene.ResourcePath, node.Path, new SceneNodeEdit(X: move.X, Y: move.Y));
            return Task.CompletedTask;
        }))
        {
            ShowEditedNode(node.Path, string.Create(CultureInfo.InvariantCulture, $"{scene.FileName}: moved {node.Name} to {move.X:0.##}, {move.Y:0.##}."));
        }
    }

    [RelayCommand]
    private async Task ApplyNodeChangesAsync()
    {
        if (_session.Draft is null || CurrentScene is not { } scene || SelectedSceneNode is not { } node)
        {
            return;
        }

        var visual = node.Visual;
        var edit = new SceneNodeEdit(
            Name: SceneNodeName != node.Name ? SceneNodeName : null,
            X: Changed(SceneNodeX, node.Transform.X),
            Y: Changed(SceneNodeY, node.Transform.Y),
            ZIndex: SceneNodeZIndex is { } zIndex && (int)zIndex != node.ZIndex ? (int)zIndex : null,
            Visible: SceneNodeVisible != node.Visible ? SceneNodeVisible : null,
            TextureId: visual is { Kind: SceneVisualKind.Texture } && SceneNodeTexture is { } texture && texture.TextureId != visual.TextureId ? texture.TextureId : null,
            Width: visual is { Kind: SceneVisualKind.Texture or SceneVisualKind.Sprite } ? Changed(SceneNodeWidth, visual.Width) : null,
            Height: visual is { Kind: SceneVisualKind.Texture or SceneVisualKind.Sprite } ? Changed(SceneNodeHeight, visual.Height) : null);
        if (edit == new SceneNodeEdit())
        {
            _session.Status = $"{node.Name}: nothing changed.";
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        var name = SceneNodeName;
        if (await _errorHandler.TryRunAsync($"Cannot change {node.Name}", () =>
        {
            _sceneEditor.EditNode(draftPath, _session.BaseUiPath, scene.ResourcePath, node.Path, edit);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"{scene.FileName}: updated {name}.";
        }
    }

    [RelayCommand]
    private void ResetSceneNodeFields()
    {
        var node = SelectedSceneNode;
        SceneNodeName = node?.Name ?? "";
        SceneNodeVisible = node?.Visible ?? false;
        SceneNodeX = node is null ? null : (decimal)node.Transform.X;
        SceneNodeY = node is null ? null : (decimal)node.Transform.Y;
        SceneNodeZIndex = node?.ZIndex;
        SceneNodeWidth = node?.Visual is { HasSize: true } sized ? (decimal)sized.Width : null;
        SceneNodeHeight = node?.Visual is { HasSize: true } sizedHeight ? (decimal)sizedHeight.Height : null;
        SceneNodeTexture = node?.Visual is { Kind: SceneVisualKind.Texture, TextureId: { } textureId }
            ? SceneTextureChoices.FirstOrDefault(choice => choice.TextureId == textureId)
            : null;
    }

    // replaces the PNG of the texture the selected node draws (every node using that texture changes)
    [RelayCommand]
    private async Task ReplaceImageAsync()
    {
        if (_session.Draft is null
            || SelectedSceneNode?.Visual is not { Kind: SceneVisualKind.Texture, TextureId: { } textureId }
            || SceneTextureChoices.FirstOrDefault(choice => choice.TextureId == textureId) is not { } texture
            || await _fileDialogs.OpenFileAsync($"Select replacement PNG for {texture.Name}", new FileType("PNG image", "*.png")) is not { } replacementPath)
        {
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        if (await _errorHandler.TryRunAsync($"Cannot replace {texture.Name}", () =>
        {
            var target = Path.Combine(draftPath, "resources", texture.RelativePath);
            var definition = new[] { draftPath, _session.BaseUiPath }
                .Where(root => root is not null)
                .Select(root => Path.Combine(root!, "resources", Path.ChangeExtension(texture.RelativePath, ".json")))
                .FirstOrDefault(File.Exists);
            _assetManager.ReplaceExtraAsset(target, replacementPath, definition);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Replaced {texture.Name}.";
        }
    }

    [RelayCommand]
    private void EditSprite()
    {
        // spriteId is "<atlas texture id>.<sprite name>"
        if (SelectedSceneNode?.Visual is { Kind: SceneVisualKind.Sprite, SpriteId: { } spriteId } && spriteId.IndexOf('.') is var dot and > 0)
        {
            var textureId = spriteId[..dot];
            SpriteRequested?.Invoke(this, new SceneSpriteRequest(textureId, spriteId[(dot + 1)..], _textureImagePaths.GetValueOrDefault(textureId)));
        }
    }

    // the scene was written: the session reloads every tab, then the edited node is selected again
    private void ShowEditedNode(string? nodePath, string status)
    {
        _session.NotifyContentChanged();
        SelectedScenePath = nodePath is not null && CurrentScene?.Find(nodePath) is not null ? nodePath : null;
        _session.Status = status;
    }

    private void Reload()
    {
        var previousImages = SceneImages;
        if (_session.Draft is null)
        {
            SceneResources = [];
            CurrentScene = null;
            SceneTextureChoices = [];
            _textureImagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            SceneImages = SceneImageSource.Empty;
            DisposeSceneImages(previousImages);
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        _textureImagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var choices = new List<SceneTextureChoice>();
        foreach (var (relativePath, textureId) in _session.Report.TextureIdsByImagePath)
        {
            var imagePath = new[] { draftPath, _session.BaseUiPath }
                .Where(root => root is not null)
                .Select(root => Path.Combine(root!, "resources", relativePath))
                .FirstOrDefault(File.Exists);
            if (imagePath is null)
            {
                continue;
            }

            _textureImagePaths.TryAdd(textureId, imagePath);
            choices.Add(new SceneTextureChoice(textureId, Path.GetFileNameWithoutExtension(relativePath), relativePath));
        }

        SceneTextureChoices = choices.OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SceneImages = new SceneImageSource(_textureImagePaths, _atlasReader.ReadAll(draftPath, _session.BaseUiPath));
        DisposeSceneImages(previousImages);

        var previousScene = SelectedSceneResource;
        var previousPath = SelectedScenePath;
        SceneResources = _sceneEditor.GetScenes(draftPath, _session.BaseUiPath);
        SelectedSceneResource = null;
        SelectedSceneResource = previousScene is not null && SceneResources.Contains(previousScene)
            ? previousScene
            : SceneResources.FirstOrDefault(scene => scene.EndsWith("/bg.scn", StringComparison.Ordinal))
                ?? (SceneResources.Count > 0 ? SceneResources[0] : null);
        if (previousPath is not null && CurrentScene?.Find(previousPath) is not null)
        {
            SelectedScenePath = previousPath;
        }
    }

    partial void OnSelectedSceneResourceChanged(string? value)
    {
        SelectedScenePath = null;
        if (_session.Draft is null || value is null)
        {
            CurrentScene = null;
            _allSceneRows = [];
            SceneRows = [];
            return;
        }

        try
        {
            CurrentScene = _sceneEditor.Load(_session.Draft.DraftPath, _session.BaseUiPath, value);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            CurrentScene = null;
            _ = _errorHandler.HandleAsync(exception, $"Cannot open {value}");
        }

        var rows = new List<SceneTreeRow>();
        if (CurrentScene is { } scene)
        {
            AddRows(scene.Root, 0, rows);
        }

        _allSceneRows = rows;
        ApplySceneSearch();
    }

    partial void OnSceneSearchTextChanged(string value)
    {
        ApplySceneSearch();
    }

    partial void OnSelectedSceneRowChanged(SceneTreeRow? value)
    {
        if (value is not null)
        {
            SelectedScenePath = value.Node.Path;
        }
    }

    partial void OnSelectedScenePathChanged(string? value)
    {
        SelectedSceneNode = value is null ? null : CurrentScene?.Find(value);
        if (SelectedSceneRow?.Node.Path != value)
        {
            SelectedSceneRow = SceneRows.FirstOrDefault(row => row.Node.Path == value);
        }
    }

    partial void OnSelectedSceneNodeChanged(SceneNode? value)
    {
        ResetSceneNodeFields();
        UpdatePreviewBranch();
    }

    partial void OnIsolateSceneBranchChanged(bool value)
    {
        UpdatePreviewBranch();
    }

    private void UpdatePreviewBranch()
    {
        // the first index of the path is the top-level branch (a menu, a background...) the node belongs to
        PreviewBranchPath = IsolateSceneBranch && SelectedSceneNode is { Path.Length: > 0 } node ? node.Path.Split('/')[0] : null;
    }

    private void ApplySceneSearch()
    {
        SceneRows = string.IsNullOrWhiteSpace(SceneSearchText)
            ? _allSceneRows
            : _allSceneRows.Where(row => row.Node.Name.Contains(SceneSearchText.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        SelectedSceneRow = SceneRows.FirstOrDefault(row => row.Node.Path == SelectedScenePath);
    }

    private static void AddRows(SceneNode node, int depth, List<SceneTreeRow> rows)
    {
        rows.Add(new SceneTreeRow(node, depth));
        foreach (var child in node.Children)
        {
            AddRows(child, depth + 1, rows);
        }
    }

    private static double? Changed(decimal? value, double current)
    {
        return value is { } number && Math.Abs((double)number - current) > 0.0001 ? (double)number : null;
    }

    private static string SpriteName(string? spriteId)
    {
        return spriteId is null ? "" : spriteId[(spriteId.IndexOf('.') + 1)..];
    }

    private static void DisposeSceneImages(SceneImageSource images)
    {
        if (!ReferenceEquals(images, SceneImageSource.Empty))
        {
            images.Dispose();
        }
    }
}
