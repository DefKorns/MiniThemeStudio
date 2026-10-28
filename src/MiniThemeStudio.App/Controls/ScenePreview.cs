using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MiniThemeStudio.Core.Scenes;

namespace MiniThemeStudio.App.Controls;

// Draws a scene as the console shows it: 1280x720 screen, origin at its centre, Y up.
// It shows the static layout of the .scn file; scripts and animations can move or hide nodes at run time.
public sealed class ScenePreview : Control
{
    public const double ScreenWidth = 1280;
    public const double ScreenHeight = 720;

    public static readonly StyledProperty<SceneNode?> RootProperty =
        AvaloniaProperty.Register<ScenePreview, SceneNode?>(nameof(Root));

    public static readonly StyledProperty<string?> SelectedPathProperty =
        AvaloniaProperty.Register<ScenePreview, string?>(nameof(SelectedPath), defaultBindingMode: BindingMode.TwoWay);

    // only this top-level branch is drawn (index of a child of the root), or the whole scene when null
    public static readonly StyledProperty<string?> BranchPathProperty =
        AvaloniaProperty.Register<ScenePreview, string?>(nameof(BranchPath));

    public static readonly StyledProperty<bool> ShowHiddenProperty =
        AvaloniaProperty.Register<ScenePreview, bool>(nameof(ShowHidden));

    public static readonly StyledProperty<SceneImageSource?> ImagesProperty =
        AvaloniaProperty.Register<ScenePreview, SceneImageSource?>(nameof(Images));

    private static readonly IBrush ScreenBrush = new SolidColorBrush(Color.FromRgb(16, 16, 20));
    private static readonly IBrush OutsideBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IPen ScreenPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1);
    private static readonly IPen MissingPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 80, 80)), 1, DashStyle.Dash);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 170, 0)), 2);
    private static readonly IPen GroupPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 170, 0)), 1, DashStyle.Dash);
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private List<DrawItem> _items = [];
    private Dictionary<string, Matrix> _worlds = [];
    private Rect _screen;
    private string? _hoveredPath;

    static ScenePreview()
    {
        AffectsRender<ScenePreview>(RootProperty, SelectedPathProperty, BranchPathProperty, ShowHiddenProperty, ImagesProperty);
    }

    public SceneNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public string? SelectedPath
    {
        get => GetValue(SelectedPathProperty);
        set => SetValue(SelectedPathProperty, value);
    }

    public string? BranchPath
    {
        get => GetValue(BranchPathProperty);
        set => SetValue(BranchPathProperty, value);
    }

    public bool ShowHidden
    {
        get => GetValue(ShowHiddenProperty);
        set => SetValue(ShowHiddenProperty, value);
    }

    public SceneImageSource? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        if (scale <= 0)
        {
            return;
        }

        _screen = new Rect((Bounds.Width - ScreenWidth * scale) / 2, (Bounds.Height - ScreenHeight * scale) / 2, ScreenWidth * scale, ScreenHeight * scale);
        var screenMatrix = new Matrix(scale, 0, 0, -scale, _screen.Center.X, _screen.Center.Y);
        Collect(screenMatrix);

        using var clip = context.PushClip(new Rect(Bounds.Size));
        context.FillRectangle(ScreenBrush, _screen);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            foreach (var item in _items)
            {
                Draw(context, item);
            }
        }

        // dim what lies outside the console screen
        context.FillRectangle(OutsideBrush, new Rect(0, 0, Bounds.Width, _screen.Top));
        context.FillRectangle(OutsideBrush, new Rect(0, _screen.Bottom, Bounds.Width, Bounds.Height - _screen.Bottom));
        context.FillRectangle(OutsideBrush, new Rect(0, _screen.Top, _screen.Left, _screen.Height));
        context.FillRectangle(OutsideBrush, new Rect(_screen.Right, _screen.Top, Bounds.Width - _screen.Right, _screen.Height));
        context.DrawRectangle(null, ScreenPen, _screen);

        if (_hoveredPath != SelectedPath && _items.FirstOrDefault(item => item.Node.Path == _hoveredPath) is { } hovered)
        {
            DrawOutline(context, Corners(hovered), HoverPen);
        }

        DrawSelection(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (FindAt(e.GetPosition(this)) is { } item)
        {
            SelectedPath = item.Node.Path;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hovered = FindAt(e.GetPosition(this));
        if (hovered?.Node.Path != _hoveredPath)
        {
            _hoveredPath = hovered?.Node.Path;
            ToolTip.SetTip(this, hovered?.Node.Name);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredPath = null;
        InvalidateVisual();
    }

    private void Collect(Matrix screenMatrix)
    {
        _items = [];
        _worlds = [];
        if (Root is not { } root)
        {
            return;
        }

        if (BranchPath is null
            || !int.TryParse(BranchPath, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index >= root.Children.Count)
        {
            Collect(root, screenMatrix, false);
            return;
        }

        var rootWorld = ToMatrix(root.Transform) * screenMatrix;
        _worlds[root.Path] = rootWorld;
        var branch = root.Children[index];
        Collect(branch, rootWorld, !root.Visible || !root.Enabled);

        // menus waiting off screen (e.g. at y=720) are slid in by scripts: show them where they are used
        if (_items.Count > 0 && !_items.SelectMany(Corners).Any(_screen.Contains))
        {
            _items = [];
            _worlds = new Dictionary<string, Matrix> { [root.Path] = rootWorld };
            Collect(branch with { Transform = branch.Transform with { X = 0, Y = 0 } }, rootWorld, !root.Visible || !root.Enabled);
        }
    }

    private void Collect(SceneNode node, Matrix parentWorld, bool hiddenAncestor)
    {
        var hidden = hiddenAncestor || !node.Visible || !node.Enabled;
        if (hidden && !ShowHidden)
        {
            return;
        }

        var world = ToMatrix(node.Transform) * parentWorld;
        _worlds[node.Path] = world;
        if (node.Visual is { } visual && CreateItem(node, visual, world, hidden) is { } item)
        {
            _items.Add(item);
        }

        foreach (var child in node.Children.OrderBy(child => child.ZIndex))
        {
            Collect(child, world, hidden);
        }
    }

    private DrawItem? CreateItem(SceneNode node, SceneVisual visual, Matrix world, bool hidden)
    {
        FormattedText? text = null;
        SceneImage? image = null;
        double width, height;
        if (visual.Kind == SceneVisualKind.Label)
        {
            if (string.IsNullOrEmpty(visual.Text))
            {
                return null;
            }

            text = new FormattedText(visual.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 22, Brushes.White);
            (width, height) = (text.Width, text.Height);
        }
        else
        {
            image = Images?.Resolve(visual);
            (width, height) = visual.HasSize
                ? (visual.Width, visual.Height)
                : image is { } resolved ? (resolved.LogicalSize.Width, resolved.LogicalSize.Height) : (0, 0);
            if (width <= 0 || height <= 0)
            {
                return null;
            }
        }

        // pivots are fractions from the top-left of the image; atlas sprites carry theirs in pixels
        var (pivotX, pivotY) = !double.IsNaN(visual.PivotX)
            ? (visual.PivotX, visual.PivotY)
            : image is { Pivot: { } pivot, LogicalSize: { Width: > 0, Height: > 0 } logical }
                ? (pivot.X / logical.Width, pivot.Y / logical.Height)
                : (0.5, 0.5);

        // image space (pixels, Y down) to node space (Y up)
        var imageToNode = new Matrix(1, 0, 0, -1, -width * pivotX, height * pivotY);
        if (visual.HorizontalFlip)
        {
            imageToNode = new Matrix(-1, 0, 0, 1, width, 0) * imageToNode;
        }

        return new DrawItem(node, visual, imageToNode * world, new Size(width, height), image, text, hidden);
    }

    private static void Draw(DrawingContext context, DrawItem item)
    {
        using var transform = context.PushTransform(item.Transform);
        using var opacity = context.PushOpacity(Math.Clamp(item.Visual.Opacity, 0, 1) * (item.Hidden ? 0.35 : 1));
        if (item.Text is { } text)
        {
            context.DrawText(text, default);
            return;
        }

        if (item.Image is not { } image)
        {
            context.DrawRectangle(null, MissingPen, new Rect(item.Size));
            return;
        }

        // uvRepeat tiles the image over the node size (wrapping walls use it)
        var columns = (int)Math.Clamp(Math.Round(item.Visual.RepeatX), 1, 16);
        var rows = (int)Math.Clamp(Math.Round(item.Visual.RepeatY), 1, 16);
        var tile = new Size(item.Size.Width / columns, item.Size.Height / rows);
        var scaleX = tile.Width / Math.Max(1, image.LogicalSize.Width);
        var scaleY = tile.Height / Math.Max(1, image.LogicalSize.Height);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var destination = new Rect(
                    column * tile.Width + image.Placement.X * scaleX,
                    row * tile.Height + image.Placement.Y * scaleY,
                    image.Placement.Width * scaleX,
                    image.Placement.Height * scaleY);
                context.DrawImage(image.Bitmap, image.Source, destination);
            }
        }
    }

    private void DrawSelection(DrawingContext context)
    {
        if (SelectedPath is not { } path || !_worlds.TryGetValue(path, out var world))
        {
            return;
        }

        if (_items.FirstOrDefault(item => item.Node.Path == path) is { } selected)
        {
            DrawOutline(context, Corners(selected), SelectionPen);
        }
        else
        {
            // a group: frame what its children draw
            var prefix = path.Length == 0 ? "" : path + "/";
            var corners = _items.Where(item => item.Node.Path.StartsWith(prefix, StringComparison.Ordinal)).SelectMany(Corners).ToList();
            if (corners.Count > 0)
            {
                var bounds = new Rect(
                    new Point(corners.Min(point => point.X), corners.Min(point => point.Y)),
                    new Point(corners.Max(point => point.X), corners.Max(point => point.Y)));
                context.DrawRectangle(null, GroupPen, bounds);
            }
        }

        var origin = world.Transform(default);
        context.DrawLine(SelectionPen, origin - new Vector(8, 0), origin + new Vector(8, 0));
        context.DrawLine(SelectionPen, origin - new Vector(0, 8), origin + new Vector(0, 8));
    }

    private static void DrawOutline(DrawingContext context, Point[] corners, IPen pen)
    {
        for (var index = 0; index < corners.Length; index++)
        {
            context.DrawLine(pen, corners[index], corners[(index + 1) % corners.Length]);
        }
    }

    private DrawItem? FindAt(Point position)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var item = _items[index];
            if (item.Transform.TryInvert(out var inverse) && new Rect(item.Size).Contains(inverse.Transform(position)))
            {
                return item;
            }
        }

        return null;
    }

    private static Point[] Corners(DrawItem item)
    {
        var size = item.Size;
        return [
            item.Transform.Transform(default),
            item.Transform.Transform(new Point(size.Width, 0)),
            item.Transform.Transform(new Point(size.Width, size.Height)),
            item.Transform.Transform(new Point(0, size.Height)),
        ];
    }

    // engine [a, b, x, c, d, y]: x' = a*px + b*py + x, y' = c*px + d*py + y
    private static Matrix ToMatrix(SceneTransform transform) =>
        new(transform.A, transform.C, transform.B, transform.D, transform.X, transform.Y);

    private sealed record DrawItem(
        SceneNode Node,
        SceneVisual Visual,
        Matrix Transform,
        Size Size,
        SceneImage? Image,
        FormattedText? Text,
        bool Hidden);
}
