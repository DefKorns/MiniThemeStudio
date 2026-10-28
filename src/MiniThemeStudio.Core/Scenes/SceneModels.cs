namespace MiniThemeStudio.Core.Scenes;

// node "transform": [a, b, x, c, d, y], so x' = a*px + b*py + x and y' = c*px + d*py + y.
// Scene units are console pixels with the origin at the centre of the 1280x720 screen and Y pointing up.
public sealed record SceneTransform(double A, double B, double X, double C, double D, double Y)
{
    public static SceneTransform Identity { get; } = new(1, 0, 0, 0, 1, 0);
}

public enum SceneVisualKind
{
    Texture,
    Sprite,
    Label,
}

// what a node draws: a whole texture (TextureComponent), an atlas sprite (SpriteComponent) or text (LabelComponent);
// OffsetX slides the texture inside the node, in widths of the node (uvOffset, which scrolling backgrounds animate)
public sealed record SceneVisual(
    SceneVisualKind Kind,
    string? TextureId,
    string? SpriteId,
    string? Text,
    double Width,
    double Height,
    double PivotX,
    double PivotY,
    double RepeatX,
    double RepeatY,
    bool HorizontalFlip,
    double Opacity,
    double OffsetX = 0)
{
    public bool HasSize => Width > 0 && Height > 0;
}

// Alpha is the node's own opacity, which its children inherit
public sealed record SceneNode(
    string Path,
    string Name,
    SceneTransform Transform,
    int ZIndex,
    bool Visible,
    bool Enabled,
    IReadOnlyList<string> ComponentTypes,
    SceneVisual? Visual,
    IReadOnlyList<SceneNode> Children,
    double Alpha = 1)
{
    public string ComponentsText => ComponentTypes.Count == 0 ? "" : string.Join(", ", ComponentTypes.Select(type => type.Replace("Component", "", StringComparison.Ordinal)));

    public IEnumerable<SceneNode> SelfAndDescendants()
    {
        yield return this;
        foreach (SceneNode? descendant in Children.SelectMany(child => child.SelfAndDescendants()))
        {
            yield return descendant;
        }
    }
}

public sealed record SceneDocument(string ResourcePath, bool IsInTheme, SceneNode Root)
{
    public string FileName => System.IO.Path.GetFileName(ResourcePath);

    public SceneNode? Find(string path) => Root.SelfAndDescendants().FirstOrDefault(node => node.Path == path);
}

// null leaves the value unchanged
public sealed record SceneNodeEdit(
    string? Name = null,
    double? X = null,
    double? Y = null,
    int? ZIndex = null,
    bool? Visible = null,
    string? TextureId = null,
    double? Width = null,
    double? Height = null);

public interface ISceneEditor
{
    // resource paths (as in dependencies.json, e.g. /prefab/bg.scn) of the scenes of the theme
    IReadOnlyList<string> GetScenes(string themePath, string? baseUiPath);

    SceneDocument Load(string themePath, string? baseUiPath, string sceneResourcePath);

    // the node is addressed by its child indexes from the root ("" is the root, "2/0" the first child of the third child)
    void EditNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath, SceneNodeEdit edit);

    // the .sceneanimation files whose tracks all name nodes of the scene, with the node that plays each one
    IReadOnlyList<SceneAnimationBinding> GetAnimations(string themePath, string? baseUiPath, SceneDocument scene);

    // registers a PNG as a new texture of the theme and returns its texture id
    string ImportTexture(string themeDraftPath, string? baseUiPath, string sourceImagePath);

    // adds a node drawing the whole texture at its own size, as the top child of parentPath; returns the new node's path
    string AddImageNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string parentPath, string textureId, string name);

    // copies the node and its children next to it, slightly offset so the copy shows; returns the copy's path
    string DuplicateNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath);

    // removes the node and its children; a node other nodes of the scene link to is kept
    void DeleteNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath);
}
