using MiniThemeStudio.Core.Scenes;

namespace MiniThemeStudio.Core.Tests.Scenes;

public sealed class SceneAnimationEvaluatorTests
{
    [Fact]
    public void HeldKeysKeepTheirValueUntilTheNextKey()
    {
        SceneAnimationTrack track = Track("Alpha", new CurveKey(0, CurveInterpolation.Constant, 1), new CurveKey(2, CurveInterpolation.Constant, 0));

        Assert.Equal(1, SceneAnimationEvaluator.Number(track, -1));
        Assert.Equal(1, SceneAnimationEvaluator.Number(track, 1.99));
        Assert.Equal(0, SceneAnimationEvaluator.Number(track, 2));
        Assert.Equal(0, SceneAnimationEvaluator.Number(track, 50));
    }

    [Fact]
    public void LinearKeysSlideToTheirEndByTheNextKey()
    {
        // lyt_blind: the width closes from 160 to 0 in 0.1 s
        SceneAnimationTrack track = Track("Width", new CurveKey(0, CurveInterpolation.Linear, 160, 0), new CurveKey(0.1, CurveInterpolation.Constant, 0));

        Assert.Equal(80, SceneAnimationEvaluator.Number(track, 0.05)!.Value, 6);
        Assert.Equal(0, SceneAnimationEvaluator.Number(track, 0.2));
    }

    [Fact]
    public void HermiteKeysWithTheLinesSlopeMoveStraight()
    {
        // lyt_wallpaper: the texture slides one width in 8.53 s, tangents 1 / 8.53 per second
        SceneAnimationTrack track = Track("TextureOffsetX", new CurveKey(0, CurveInterpolation.Hermite, 0, 1, 0.1171875, 0.1171875), new CurveKey(8.533333333333333, CurveInterpolation.Constant, 1));

        Assert.Equal(0.25, SceneAnimationEvaluator.Number(track, 8.533333333333333 / 4)!.Value, 6);
        Assert.Equal(0.5, SceneAnimationEvaluator.Number(track, 8.533333333333333 / 2)!.Value, 6);
    }

    [Fact]
    public void HermiteTangentsShapeTheCurve()
    {
        // lyt_hit: leaves at 780 px/s and lands softly
        SceneAnimationTrack track = Track("LocalPositionY", new CurveKey(0, CurveInterpolation.Hermite, 0, 65, 780, 0), new CurveKey(0.2, CurveInterpolation.Constant, 65));

        double early = SceneAnimationEvaluator.Number(track, 0.02)!.Value;
        Assert.InRange(early, 65 * 0.1, 65);
        Assert.Equal(65, SceneAnimationEvaluator.Number(track, 0.2));
    }

    [Fact]
    public void ApplyMovesTurnsAndFadesTheNamedNodes()
    {
        SceneNode hero = Node("1/0", "Sprite_Hero_0", visual: Visual("t1"));
        SceneNode pane = Node("1", "N", children: [hero]);
        SceneNode root = Node("", "demo", components: ["AnimatorComponent"], children: [Node("0", "other"), pane]);
        var clip = new SceneAnimationClip("/layout/lyt_hero@run.sceneanimation", 1,
        [
            Track(["N", "Sprite_Hero_0"], "LocalPositionX", new CurveKey(0, CurveInterpolation.Linear, 0, 100), new CurveKey(1, CurveInterpolation.Constant, 100)),
            Track(["N", "Sprite_Hero_0"], "LocalRotation", new CurveKey(0, CurveInterpolation.Constant, Math.PI / 2)),
            Track(["N", "Sprite_Hero_0"], "Texture", new CurveKey(0, CurveInterpolation.Constant, 0, Text: "t1"), new CurveKey(0.5, CurveInterpolation.Constant, 0, Text: "t2")),
            Track(["N"], "Alpha", new CurveKey(0, CurveInterpolation.Constant, 0.5)),
            Track(["N"], "Visible", new CurveKey(0, CurveInterpolation.Constant, 0, Flag: false)),
            Track(["gone"], "LocalPositionY", new CurveKey(0, CurveInterpolation.Constant, 5)),
        ]);

        SceneNode animated = SceneAnimationEvaluator.Apply(root, new SceneAnimationBinding(clip, "", "demo"), 0.75);

        SceneNode animatedHero = animated.SelfAndDescendants().Single(node => node.Path == "1/0");
        Assert.Equal(75, animatedHero.Transform.X, 6);
        Assert.Equal((0d, -1d, 1d, 0d), (Math.Round(animatedHero.Transform.A, 6), Math.Round(animatedHero.Transform.B, 6), Math.Round(animatedHero.Transform.C, 6), Math.Round(animatedHero.Transform.D, 6)));
        Assert.Equal("t2", animatedHero.Visual!.TextureId);
        SceneNode animatedPane = animated.Children[1];
        Assert.Equal(0.5, animatedPane.Alpha);
        Assert.False(animatedPane.Visible);

        // the scene itself is not changed
        Assert.Equal(0, hero.Transform.X);
        Assert.Same(root.Children[0], root.Children[0]);
    }

    [Fact]
    public void ScaleOnlyKeepsTheFlipOfTheNode()
    {
        // characters face left with a negative X scale
        SceneNode flipped = Node("0", "hero") with { Transform = new SceneTransform(-2, 0, 0, 0, 2, 0) };
        SceneNode root = Node("", "demo", children: [flipped]);
        var clip = new SceneAnimationClip("/a.sceneanimation", 1, [Track(["hero"], "LocalScaleY", new CurveKey(0, CurveInterpolation.Constant, 3))]);

        SceneNode animated = SceneAnimationEvaluator.Apply(root, new SceneAnimationBinding(clip, "", "demo"), 0).Children[0];

        Assert.Equal(3, Math.Abs(animated.Transform.D), 6);
        Assert.Equal(2, Math.Abs(animated.Transform.A), 6);
        Assert.True(animated.Transform.A * animated.Transform.D < 0);
    }

    private static SceneAnimationTrack Track(string property, params CurveKey[] keys) => Track(["node"], property, keys);

    private static SceneAnimationTrack Track(string[] path, string property, params CurveKey[] keys) => new(path, property, null, keys);

    private static SceneVisual Visual(string textureId) =>
        new(SceneVisualKind.Texture, textureId, null, null, 24, 32, 0.5, 0.5, 1, 1, false, 1);

    private static SceneNode Node(string path, string name, SceneVisual? visual = null, string[]? components = null, SceneNode[]? children = null) =>
        new(path, name, SceneTransform.Identity, 0, true, true, components ?? [], visual, children ?? []);
}
