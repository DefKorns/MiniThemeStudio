using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.App.Controls;
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

public partial class MainWindowViewModel
{
    private readonly ISceneEditor _sceneEditor;
    private IReadOnlyList<SceneTreeRow> _allSceneRows = [];
    private Dictionary<string, string> _textureImagePaths = new(StringComparer.Ordinal);

    [ObservableProperty]
    private IReadOnlyList<string> sceneResources = [];

    [ObservableProperty]
    private string? selectedSceneResource;

    [ObservableProperty]
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

    // the editable copy of the selected node, written by ApplySceneNodeChanges
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

    public bool ApplySceneNodeChanges()
    {
        if (_draft is null || CurrentScene is not { } scene || SelectedSceneNode is not { } node)
        {
            Status = "Select a scene node first.";
            return false;
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
            Status = $"{node.Name}: nothing changed.";
            return false;
        }

        try
        {
            _sceneEditor.EditNode(_draft.DraftPath, _currentBaseUiPath, scene.ResourcePath, node.Path, edit);
            MarkChanged();
            ReloadAssets();
            RunValidation();
            Status = $"{scene.FileName}: updated {SceneNodeName}.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    public void ResetSceneNodeFields()
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
    public bool ReplaceSelectedSceneImage(string replacementImagePath)
    {
        if (_draft is null || SelectedSceneNode?.Visual is not { Kind: SceneVisualKind.Texture, TextureId: { } textureId }
            || SceneTextureChoices.FirstOrDefault(choice => choice.TextureId == textureId) is not { } texture)
        {
            Status = "Select a node that draws an image.";
            return false;
        }

        try
        {
            var target = Path.Combine(_draft.DraftPath, "resources", texture.RelativePath);
            var definition = new[] { _draft.DraftPath, _currentBaseUiPath }
                .Where(root => root is not null)
                .Select(root => Path.Combine(root!, "resources", Path.ChangeExtension(texture.RelativePath, ".json")))
                .FirstOrDefault(File.Exists);
            _assetManager.ReplaceExtraAsset(target, replacementImagePath, definition);
            MarkChanged();
            ReloadAssets();
            RunValidation();
            Status = $"Replaced {texture.Name}.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    // selects the atlas sprite the selected node draws in the Sprites tab
    public bool SelectSceneSpriteInAtlas()
    {
        if (SelectedSceneNode?.Visual is not { Kind: SceneVisualKind.Sprite, SpriteId: { } spriteId })
        {
            return false;
        }

        var dot = spriteId.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        var textureId = spriteId[..dot];
        var name = spriteId[(dot + 1)..];
        var imagePath = _textureImagePaths.GetValueOrDefault(textureId);
        var atlas = Atlases.FirstOrDefault(candidate => candidate.TextureId == textureId)
            ?? Atlases.FirstOrDefault(candidate => imagePath is not null && PathsEqual(candidate.ImagePath, imagePath));
        if (atlas?.Sprites.FirstOrDefault(sprite => sprite.Name == name) is not { } sprite)
        {
            Status = $"Sprite {name} was not found in the atlases.";
            return false;
        }

        SelectedAtlas = atlas;
        SpriteSearchText = "";
        SelectedSprite = sprite;
        return true;
    }

    private void ReloadScenes(ThemeResourceReport? report)
    {
        var previousImages = SceneImages;
        if (_draft is null || report is null)
        {
            SceneResources = [];
            CurrentScene = null;
            SceneTextureChoices = [];
            _textureImagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            SceneImages = SceneImageSource.Empty;
            DisposeSceneImages(previousImages);
            return;
        }

        _textureImagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var choices = new List<SceneTextureChoice>();
        foreach (var (relativePath, textureId) in report.TextureIdsByImagePath)
        {
            var imagePath = new[] { _draft.DraftPath, _currentBaseUiPath }
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
        SceneImages = new SceneImageSource(_textureImagePaths, Atlases);
        DisposeSceneImages(previousImages);

        var previousScene = SelectedSceneResource;
        var previousPath = SelectedScenePath;
        SceneResources = _sceneEditor.GetScenes(_draft.DraftPath, _currentBaseUiPath);
        SelectedSceneResource = null;
        SelectedSceneResource = previousScene is not null && SceneResources.Contains(previousScene)
            ? previousScene
            : SceneResources.FirstOrDefault(scene => scene.EndsWith("/bg.scn", StringComparison.Ordinal)) ?? SceneResources.FirstOrDefault();
        if (previousPath is not null && CurrentScene?.Find(previousPath) is not null)
        {
            SelectedScenePath = previousPath;
        }
    }

    partial void OnSelectedSceneResourceChanged(string? value)
    {
        SelectedScenePath = null;
        if (_draft is null || value is null)
        {
            CurrentScene = null;
            _allSceneRows = [];
            SceneRows = [];
            return;
        }

        try
        {
            CurrentScene = _sceneEditor.Load(_draft.DraftPath, _currentBaseUiPath, value);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            CurrentScene = null;
            Status = exception.Message;
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
