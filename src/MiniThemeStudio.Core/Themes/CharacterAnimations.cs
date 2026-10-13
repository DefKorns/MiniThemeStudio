namespace MiniThemeStudio.Core.Themes;

public sealed record AnimationKey(double Time, string TextureId);

public sealed record AnimationTrack(string NodeName, IReadOnlyList<AnimationKey> Keys);

public sealed record CharacterAnimation(string ResourcePath, string Name, double Duration, IReadOnlyList<AnimationTrack> Tracks);

public sealed record CharacterNode(string Scene, string NodeName, double Width, double Height, double PivotX, double PivotY);

public sealed record CharacterTexture(string TextureId, string Name, string ImagePath, int Width, int Height);

public interface ICharacterAnimationEditor
{
    IReadOnlyList<string> GetCharacters(string themePath, string? baseUiPath);

    IReadOnlyList<CharacterAnimation> GetAnimations(string themePath, string? baseUiPath, string character);

    IReadOnlyList<CharacterNode> GetNodes(string themePath, string? baseUiPath, string character);

    IReadOnlyDictionary<string, CharacterTexture> GetTextures(string themePath, string? baseUiPath);

    void MirrorTextures(string themeDraftPath, string? baseUiPath, IReadOnlyCollection<string> textureIds);

    string AddTexture(string themeDraftPath, string? baseUiPath, string relativeImagePath, string sourceImagePath);

    void SetTrackKeys(string themeDraftPath, string? baseUiPath, string animationResourcePath, string nodeName, IReadOnlyList<AnimationKey> keys, double duration);

    void SetFrameSize(string themeDraftPath, string? baseUiPath, string character, AtlasSize frameSize);
}
