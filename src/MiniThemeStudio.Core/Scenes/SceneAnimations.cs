namespace MiniThemeStudio.Core.Scenes;

public enum CurveInterpolation
{
    // the key's value holds until the next key
    Constant,

    // from From to To by the next key
    Linear,

    // a cubic from From to To by the next key, leaving with TangentOut and arriving with TangentIn (units per second)
    Hermite,
}

// one key of a .sceneanimation track; numbers use From (and To, the tangents), textures Text, visibility Flag
public sealed record CurveKey(
    double Time,
    CurveInterpolation Interpolation,
    double From,
    double To = 0,
    double TangentOut = 0,
    double TangentIn = 0,
    string? Text = null,
    bool? Flag = null);

// NodePath names the node from the node that plays the animation, e.g. ["N", "Sprite_Hero_0"]
public sealed record SceneAnimationTrack(IReadOnlyList<string> NodePath, string PropertyId, string? ComponentType, IReadOnlyList<CurveKey> Keys);

public sealed record SceneAnimationClip(string ResourcePath, double Duration, IReadOnlyList<SceneAnimationTrack> Tracks)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(ResourcePath);
}

// an animation and the node of a scene that plays it (AnimatorPath, as in SceneNode.Path)
public sealed record SceneAnimationBinding(SceneAnimationClip Clip, string AnimatorPath, string AnimatorName)
{
    public string DisplayName => $"{Clip.Name} ({AnimatorName})";
}

// Plays a .sceneanimation over a scene: the value of each track at a time, and the scene as it looks then.
public static class SceneAnimationEvaluator
{
    public static double? Number(SceneAnimationTrack track, double time)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (KeyAt(track.Keys, time) is not ({ } key, var next))
        {
            return null;
        }

        if (time < key.Time || next is null || key.Interpolation == CurveInterpolation.Constant)
        {
            // before the first key, after the last one, or between two held values
            return key.Interpolation == CurveInterpolation.Constant || time < key.Time ? key.From : key.To;
        }

        double span = next.Time - key.Time;
        double s = span > 0 ? Math.Clamp((time - key.Time) / span, 0, 1) : 1;
        if (key.Interpolation == CurveInterpolation.Linear)
        {
            return key.From + ((key.To - key.From) * s);
        }

        double s2 = s * s;
        double s3 = s2 * s;
        return (((2 * s3) - (3 * s2) + 1) * key.From)
            + ((s3 - (2 * s2) + s) * span * key.TangentOut)
            + (((-2 * s3) + (3 * s2)) * key.To)
            + ((s3 - s2) * span * key.TangentIn);
    }

    public static CurveKey? Held(SceneAnimationTrack track, double time)
    {
        ArgumentNullException.ThrowIfNull(track);
        return KeyAt(track.Keys, time)?.Key;
    }

    // the scene with every animated property as it is at the time; tracks naming missing nodes are skipped
    public static SceneNode Apply(SceneNode root, SceneAnimationBinding binding, double time)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(binding);
        if (root.SelfAndDescendants().FirstOrDefault(node => node.Path == binding.AnimatorPath) is not { } animator)
        {
            return root;
        }

        var tracksByNode = binding.Clip.Tracks
            .Select(track => (Track: track, Node: Resolve(animator, track.NodePath)))
            .Where(entry => entry.Node is not null)
            .GroupBy(entry => entry.Node!.Path, entry => entry.Track, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        return tracksByNode.Count == 0 ? root : Rebuild(root, tracksByNode, time);
    }

    public static SceneNode? Resolve(SceneNode animator, IReadOnlyList<string> nodePath)
    {
        ArgumentNullException.ThrowIfNull(animator);
        ArgumentNullException.ThrowIfNull(nodePath);
        SceneNode? node = animator;
        foreach (string name in nodePath)
        {
            node = node.Children.FirstOrDefault(child => child.Name == name);
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    private static SceneNode Rebuild(SceneNode node, Dictionary<string, List<SceneAnimationTrack>> tracksByNode, double time)
    {
        var children = node.Children.Select(child => Rebuild(child, tracksByNode, time)).ToList();
        SceneNode animated = node with { Children = children };
        return tracksByNode.TryGetValue(node.Path, out List<SceneAnimationTrack>? tracks) ? Animate(animated, tracks, time) : animated;
    }

    private static SceneNode Animate(SceneNode node, List<SceneAnimationTrack> tracks, double time)
    {
        SceneTransform transform = node.Transform;

        // scale and rotation are rebuilt from what the matrix holds, so the ones not animated are kept;
        // a mirrored node is read as a negative X scale, as the console flips its characters
        double determinant = (transform.A * transform.D) - (transform.B * transform.C);
        double scaleX = Math.Sqrt((transform.A * transform.A) + (transform.C * transform.C)) * (determinant < 0 ? -1 : 1);
        double rotation = determinant < 0 ? Math.Atan2(-transform.C, -transform.A) : Math.Atan2(transform.C, transform.A);
        double scaleY = scaleX > 0 ? determinant / scaleX : transform.D;
        double x = transform.X;
        double y = transform.Y;
        bool visible = node.Visible;
        double alpha = node.Alpha;
        SceneVisual? visual = node.Visual;
        bool reshaped = false;

        foreach (SceneAnimationTrack track in tracks)
        {
            switch (track.PropertyId)
            {
                case "LocalPositionX": x = Number(track, time) ?? x; break;
                case "LocalPositionY": y = Number(track, time) ?? y; break;
                case "LocalScaleX": scaleX = Number(track, time) ?? scaleX; reshaped = true; break;
                case "LocalScaleY": scaleY = Number(track, time) ?? scaleY; reshaped = true; break;
                case "LocalRotation": rotation = Number(track, time) ?? rotation; reshaped = true; break;
                case "Alpha": alpha = Number(track, time) ?? alpha; break;
                case "Visible": visible = Held(track, time)?.Flag ?? visible; break;
                case "Texture" when visual is not null && Held(track, time)?.Text is { } textureId: visual = visual with { TextureId = textureId }; break;
                case "Width" when visual is not null: visual = visual with { Width = Number(track, time) ?? visual.Width }; break;
                case "TextureOffsetX" when visual is not null: visual = visual with { OffsetX = Number(track, time) ?? visual.OffsetX }; break;
                default: break;
            }
        }

        (double sin, double cos) = Math.SinCos(rotation);
        SceneTransform animatedTransform = reshaped
            ? new SceneTransform(scaleX * cos, -scaleY * sin, x, scaleX * sin, scaleY * cos, y)
            : transform with { X = x, Y = y };
        return node with { Transform = animatedTransform, Visible = visible, Alpha = alpha, Visual = visual };
    }

    // the last key at or before the time (the first one before it starts) and the key after it
    private static (CurveKey Key, CurveKey? Next)? KeyAt(IReadOnlyList<CurveKey> keys, double time)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        int index = 0;
        while (index + 1 < keys.Count && keys[index + 1].Time <= time)
        {
            index++;
        }

        return (keys[index], index + 1 < keys.Count ? keys[index + 1] : null);
    }
}
