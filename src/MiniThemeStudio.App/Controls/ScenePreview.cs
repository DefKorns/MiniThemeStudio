using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MiniThemeStudio.Core.Scenes;

namespace MiniThemeStudio.App.Controls;

// where a dragged node was dropped: its new X and Y in its parent's space
public sealed record SceneNodeMove(string Path, double X, double Y);

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

    // the colour each palette colour name has; a label named by one is drawn with it, white otherwise
    public static readonly StyledProperty<IReadOnlyDictionary<string, Color>?> ColorsProperty =
        AvaloniaProperty.Register<ScenePreview, IReadOnlyDictionary<string, Color>?>(nameof(Colors));

    // the view is framed on the labels drawn with this palette colour: on the whole group of nodes when it is small
    // (a card, a button, an item with its picture), else on a margin around the text, so a balloon or a bar the text
    // sits on shows only in part. It shows no screen around it, and is as tall as its width needs
    public static readonly StyledProperty<string?> FocusColorNameProperty =
        AvaloniaProperty.Register<ScenePreview, string?>(nameof(FocusColorName));

    // a text whose middle is over a small icon is moved to the right of it, as the console's scripts lay out a button and its
    // name (the scene keeps both at the same place)
    public static readonly StyledProperty<bool> TextBesideIconsProperty =
        AvaloniaProperty.Register<ScenePreview, bool>(nameof(TextBesideIcons));

    // the words of the "@key" labels of the scene, as the console shows them
    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> StringsProperty =
        AvaloniaProperty.Register<ScenePreview, IReadOnlyDictionary<string, string>?>(nameof(Strings));

    // how much of itself a hidden node keeps when ShowHidden draws it
    public static readonly StyledProperty<double> HiddenOpacityProperty =
        AvaloniaProperty.Register<ScenePreview, double>(nameof(HiddenOpacity), 0.35);

    // executed with a SceneNodeMove when a dragged node is dropped; nodes cannot be dragged without it
    public static readonly StyledProperty<ICommand?> MoveCommandProperty =
        AvaloniaProperty.Register<ScenePreview, ICommand?>(nameof(MoveCommand));

    // screen pixels the pointer must travel before a press becomes a drag
    private const double DragThreshold = 3;

    // screen units: the margin around the framed text (wide enough for the icon beside a title), and how far the
    // framed part is enlarged
    private const double FocusMarginX = 64;
    private const double FocusMarginY = 24;
    private const double MaxFocusScale = 2;

    // a group of nodes no bigger than this is shown whole, with a small margin; the heights the control takes
    private const double MaxWholeGroupWidth = 520;
    private const double MaxWholeGroupHeight = 340;
    private const double WholeGroupMargin = 8;
    private const double MinFocusHeight = 96;
    private const double MaxFocusHeight = 280;

    // screen units: the biggest picture that is an icon beside a text, and the space between the icon and the text
    private const double MaxIconSize = 96;
    private const double IconGap = 8;

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
    private Matrix _screenMatrix = Matrix.Identity;
    private string? _hoveredPath;
    private Drag? _drag;

    static ScenePreview()
    {
        AffectsRender<ScenePreview>(RootProperty, SelectedPathProperty, BranchPathProperty, ShowHiddenProperty, ImagesProperty, ColorsProperty, StringsProperty, HiddenOpacityProperty, FocusColorNameProperty, TextBesideIconsProperty);
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

    public IReadOnlyDictionary<string, Color>? Colors
    {
        get => GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    public IReadOnlyDictionary<string, string>? Strings
    {
        get => GetValue(StringsProperty);
        set => SetValue(StringsProperty, value);
    }

    public double HiddenOpacity
    {
        get => GetValue(HiddenOpacityProperty);
        set => SetValue(HiddenOpacityProperty, value);
    }

    public bool TextBesideIcons
    {
        get => GetValue(TextBesideIconsProperty);
        set => SetValue(TextBesideIconsProperty, value);
    }

    public string? FocusColorName
    {
        get => GetValue(FocusColorNameProperty);
        set => SetValue(FocusColorNameProperty, value);
    }

    public ICommand? MoveCommand
    {
        get => GetValue(MoveCommandProperty);
        set => SetValue(MoveCommandProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (FocusColorName is { Length: > 0 } focus && double.IsFinite(availableSize.Width) && FocusBox(focus) is { } box)
        {
            return new Size(availableSize.Width, Math.Clamp(availableSize.Width * box.Height / box.Width, MinFocusHeight, MaxFocusHeight));
        }

        return base.MeasureOverride(availableSize);
    }

    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        if (scale <= 0)
        {
            return;
        }

        _screen = new Rect((Bounds.Width - ScreenWidth * scale) / 2, (Bounds.Height - ScreenHeight * scale) / 2, ScreenWidth * scale, ScreenHeight * scale);
        _screenMatrix = new Matrix(scale, 0, 0, -scale, _screen.Center.X, _screen.Center.Y);
        bool framed = false;
        if (FocusColorName is { Length: > 0 } focus && FocusView(focus, Bounds.Size) is { } focused)
        {
            (_screenMatrix, _screen) = focused;
            framed = true;
        }

        Collect(_screenMatrix);
        if (TextBesideIcons)
        {
            PlaceTextBesideIcons(_screenMatrix.M11);
        }

        using DrawingContext.PushedState clip = context.PushClip(new Rect(Bounds.Size));
        if (!framed)
        {
            context.FillRectangle(ScreenBrush, _screen);
        }

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            foreach (DrawItem item in _items)
            {
                Draw(context, item, HiddenOpacity);
            }
        }

        if (!framed)
        {
            // dim what lies outside the console screen
            context.FillRectangle(OutsideBrush, new Rect(0, 0, Bounds.Width, _screen.Top));
            context.FillRectangle(OutsideBrush, new Rect(0, _screen.Bottom, Bounds.Width, Bounds.Height - _screen.Bottom));
            context.FillRectangle(OutsideBrush, new Rect(0, _screen.Top, _screen.Left, _screen.Height));
            context.FillRectangle(OutsideBrush, new Rect(_screen.Right, _screen.Top, Bounds.Width - _screen.Right, _screen.Height));
            context.DrawRectangle(null, ScreenPen, _screen);
        }

        if (_hoveredPath != SelectedPath && _items.FirstOrDefault(item => item.Node.Path == _hoveredPath) is { } hovered)
        {
            DrawOutline(context, Corners(hovered), HoverPen);
        }

        DrawSelection(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Point position = e.GetPosition(this);
        if (FindAt(position) is not { } item)
        {
            return;
        }

        e.Handled = true;

        // pressing inside the selected group drags the whole group; anything else is selected and dragged alone
        string clicked = item.Node.Path;
        string target = SelectedPath is { Length: > 0 } selected && (clicked == selected || clicked.StartsWith(selected + "/", StringComparison.Ordinal))
            ? selected
            : clicked;
        SelectedPath = target;
        if (MoveCommand is null
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || Root?.SelfAndDescendants().FirstOrDefault(node => node.Path == target) is not { } node
            || !ParentWorld(target).TryInvert(out Matrix toParent))
        {
            return;
        }

        _drag = new Drag(target, clicked, position, toParent, node.Transform.X, node.Transform.Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        e.Pointer.Capture(null);
        Cursor = null;
        if (!drag.IsMoving)
        {
            // a click without a drag: the image under the pointer, even inside the selected group
            SelectedPath = drag.ClickedPath;
            return;
        }

        var move = new SceneNodeMove(drag.Path, drag.OriginX + drag.Offset.X, drag.OriginY + drag.Offset.Y);
        if (drag.Offset != default && MoveCommand?.CanExecute(move) == true)
        {
            MoveCommand.Execute(move);
        }

        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag is not null)
        {
            _drag = null;
            Cursor = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is { } drag)
        {
            Point position = e.GetPosition(this);
            if (!drag.IsMoving && Point.Distance(position, drag.Start) < DragThreshold)
            {
                return;
            }

            // whole pixels in the parent's space, Y up, whatever the zoom of the preview
            Point delta = drag.ToParent.Transform(position) - drag.ToParent.Transform(drag.Start);
            drag.IsMoving = true;
            drag.Offset = new Vector(Math.Round(drag.OriginX + delta.X) - drag.OriginX, Math.Round(drag.OriginY + delta.Y) - drag.OriginY);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            ToolTip.SetTip(this, string.Create(CultureInfo.InvariantCulture, $"{drag.OriginX + drag.Offset.X:0.##}, {drag.OriginY + drag.Offset.Y:0.##}"));
            InvalidateVisual();
            return;
        }

        DrawItem? hovered = FindAt(e.GetPosition(this));
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

    // the part of the screen, in screen units, that shows the labels drawn with the colour; null when there are none
    private Rect? FocusBox(string colorName)
    {
        Collect(new Matrix(1, 0, 0, -1, ScreenWidth / 2, ScreenHeight / 2));
        if (TextBesideIcons)
        {
            PlaceTextBesideIcons(1);
        }

        Rect? labels = null;
        Rect? everything = null;
        foreach (DrawItem item in _items)
        {
            Rect box = BoxOf(item);
            everything = everything is { } all ? all.Union(box) : box;
            if (item.Visual.ColorName == colorName)
            {
                labels = labels is { } known ? known.Union(box) : box;
            }
        }

        if (labels is not { } found || everything is not { } whole)
        {
            return null;
        }

        return whole.Width <= MaxWholeGroupWidth && whole.Height <= MaxWholeGroupHeight
            ? whole.Inflate(WholeGroupMargin)
            : new Rect(found.X - FocusMarginX, found.Y - FocusMarginY, found.Width + 2 * FocusMarginX, found.Height + 2 * FocusMarginY);
    }

    // the matrix and the screen rectangle that frame the labels drawn with the colour, or null when there are none
    private (Matrix Matrix, Rect Screen)? FocusView(string colorName, Size bounds)
    {
        if (FocusBox(colorName) is not { } framed)
        {
            return null;
        }

        double scale = Math.Min(Math.Min(bounds.Width / framed.Width, bounds.Height / framed.Height), MaxFocusScale);
        if (scale <= 0 || double.IsInfinity(scale))
        {
            return null;
        }

        var matrix = new Matrix(scale, 0, 0, -scale, bounds.Width / 2 + scale * (ScreenWidth / 2 - framed.Center.X), bounds.Height / 2 + scale * (ScreenHeight / 2 - framed.Center.Y));
        var screen = new Rect(bounds.Width / 2 - scale * framed.Center.X, bounds.Height / 2 - scale * framed.Center.Y, ScreenWidth * scale, ScreenHeight * scale);
        return (matrix, screen);
    }

    // moves each text whose middle is over a small icon to the right of the icon; the pieces of a frame, which only touch
    // the text, stay where they are; scale is device pixels per screen unit
    private void PlaceTextBesideIcons(double scale)
    {
        for (int index = 0; index < _items.Count; index++)
        {
            DrawItem label = _items[index];
            if (label.Text is null)
            {
                continue;
            }

            Rect box = BoxOf(label);
            double right = double.NegativeInfinity;
            foreach (DrawItem icon in _items.Where(item => item.Text is null))
            {
                Rect iconBox = BoxOf(icon);
                if (iconBox.Width <= MaxIconSize * scale && iconBox.Height <= MaxIconSize * scale && iconBox.Contains(box.Center))
                {
                    right = Math.Max(right, iconBox.Right);
                }
            }

            if (!double.IsNegativeInfinity(right))
            {
                _items[index] = label with { Transform = label.Transform * Matrix.CreateTranslation(right + IconGap * scale - box.Left, 0) };
            }
        }
    }

    private static Rect BoxOf(DrawItem item)
    {
        Point[] corners = Corners(item);
        return new Rect(new Point(corners.Min(point => point.X), corners.Min(point => point.Y)), new Point(corners.Max(point => point.X), corners.Max(point => point.Y)));
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
            || !int.TryParse(BranchPath, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index >= root.Children.Count)
        {
            Collect(root, screenMatrix, false, 1);
            return;
        }

        Matrix rootWorld = ToMatrix(root.Transform) * screenMatrix;
        _worlds[root.Path] = rootWorld;
        SceneNode branch = root.Children[index];
        Collect(branch, rootWorld, !root.Visible || !root.Enabled, root.Alpha);

        // menus waiting off screen (e.g. at y=720) are slid in by scripts: show them where they are used
        if (_items.Count > 0 && !_items.SelectMany(Corners).Any(_screen.Contains))
        {
            _items = [];
            _worlds = new Dictionary<string, Matrix> { [root.Path] = rootWorld };
            Collect(branch with { Transform = branch.Transform with { X = 0, Y = 0 } }, rootWorld, !root.Visible || !root.Enabled, root.Alpha);
        }
    }

    // alpha is what the node's ancestors leave of its opacity
    private void Collect(SceneNode node, Matrix parentWorld, bool hiddenAncestor, double alpha)
    {
        bool hidden = hiddenAncestor || !node.Visible || !node.Enabled;
        if (hidden && !ShowHidden)
        {
            return;
        }

        SceneTransform transform = _drag is { IsMoving: true } drag && drag.Path == node.Path
            ? node.Transform with { X = node.Transform.X + drag.Offset.X, Y = node.Transform.Y + drag.Offset.Y }
            : node.Transform;
        Matrix world = ToMatrix(transform) * parentWorld;
        _worlds[node.Path] = world;
        alpha *= Math.Clamp(node.Alpha, 0, 1);
        if (node.Visual is { } visual && CreateItem(node, visual, world, hidden, alpha) is { } item)
        {
            _items.Add(item);
        }

        // at the same depth a text is drawn after the pictures (the balloon a text sits on can come after it in the file)
        foreach (SceneNode? child in node.Children.OrderBy(child => child.ZIndex).ThenBy(child => child.Visual?.Kind == SceneVisualKind.Label ? 1 : 0))
        {
            Collect(child, world, hidden, alpha);
        }
    }

    // "@sys_resume_Title" is "Suspend Point List" when the strings are known
    private string? ReadableText(string? text) =>
        text is { Length: > 1 } && text[0] == '@' && Strings?.TryGetValue(text[1..], out string? words) == true ? words : text;

    private IBrush LabelBrush(SceneVisual visual) =>
        visual.ColorName is { } name && Colors?.TryGetValue(name, out Color color) == true ? new SolidColorBrush(color) : Brushes.White;

    private DrawItem? CreateItem(SceneNode node, SceneVisual visual, Matrix world, bool hidden, double alpha)
    {
        FormattedText? text = null;
        SceneImage? image = null;
        double width, height;
        if (visual.Kind == SceneVisualKind.Label)
        {
            string? words = ReadableText(visual.Text);
            if (string.IsNullOrEmpty(words))
            {
                return null;
            }

            text = new FormattedText(words, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 22, LabelBrush(visual));
            text.TextAlignment = visual.Alignment switch { "Left" => TextAlignment.Left, "Right" => TextAlignment.Right, _ => TextAlignment.Center };
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
        (double pivotX, double pivotY) = !double.IsNaN(visual.PivotX)
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

        return new DrawItem(node, visual, imageToNode * world, new Size(width, height), image, text, hidden, alpha);
    }

    private static void Draw(DrawingContext context, DrawItem item, double hiddenOpacity)
    {
        using DrawingContext.PushedState transform = context.PushTransform(item.Transform);
        using DrawingContext.PushedState opacity = context.PushOpacity(Math.Clamp(item.Visual.Opacity, 0, 1) * item.Alpha * (item.Hidden ? hiddenOpacity : 1));
        if (item.Text is { } text)
        {
            context.DrawText(text, default);
            return;
        }

        if (item.Image is not { } image)
        {
            // a sprite with no picture is a plain rectangle of its colour
            if (item.Visual.Fill is { } fill)
            {
                var color = Color.FromRgb((byte)Math.Round(Math.Clamp(fill.Red, 0, 1) * 255), (byte)Math.Round(Math.Clamp(fill.Green, 0, 1) * 255), (byte)Math.Round(Math.Clamp(fill.Blue, 0, 1) * 255));
                context.DrawRectangle(new SolidColorBrush(color), null, new Rect(item.Size));
            }
            else
            {
                context.DrawRectangle(null, MissingPen, new Rect(item.Size));
            }

            return;
        }

        // uvRepeat tiles the image over the node size (wrapping walls use it)
        int columns = (int)Math.Clamp(Math.Round(item.Visual.RepeatX), 1, 16);
        int rows = (int)Math.Clamp(Math.Round(item.Visual.RepeatY), 1, 16);
        var tile = new Size(item.Size.Width / columns, item.Size.Height / rows);
        double scaleX = tile.Width / Math.Max(1, image.LogicalSize.Width);
        double scaleY = tile.Height / Math.Max(1, image.LogicalSize.Height);

        // uvOffset slides the texture inside the node (scrolling backgrounds animate it): the tiles move left and
        // wrap around, so one more column fills the gap they leave
        double shift = item.Visual.OffsetX - Math.Floor(item.Visual.OffsetX);
        using DrawingContext.PushedState? clip = shift > 0 ? context.PushClip(new Rect(item.Size)) : default(DrawingContext.PushedState?);
        int lastColumn = shift > 0 ? columns : columns - 1;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column <= lastColumn; column++)
            {
                var destination = new Rect(
                    (column - shift) * tile.Width + image.Placement.X * scaleX,
                    row * tile.Height + image.Placement.Y * scaleY,
                    image.Placement.Width * scaleX,
                    image.Placement.Height * scaleY);
                context.DrawImage(image.Bitmap, image.Source, destination);
            }
        }
    }

    private void DrawSelection(DrawingContext context)
    {
        if (SelectedPath is not { } path || !_worlds.TryGetValue(path, out Matrix world))
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
            string prefix = path.Length == 0 ? "" : path + "/";
            var corners = _items.Where(item => item.Node.Path.StartsWith(prefix, StringComparison.Ordinal)).SelectMany(Corners).ToList();
            if (corners.Count > 0)
            {
                var bounds = new Rect(
                    new Point(corners.Min(point => point.X), corners.Min(point => point.Y)),
                    new Point(corners.Max(point => point.X), corners.Max(point => point.Y)));
                context.DrawRectangle(null, GroupPen, bounds);
            }
        }

        Point origin = world.Transform(default);
        context.DrawLine(SelectionPen, origin - new Vector(8, 0), origin + new Vector(8, 0));
        context.DrawLine(SelectionPen, origin - new Vector(0, 8), origin + new Vector(0, 8));
    }

    // the world matrix a node's transform is applied to: its parent's, or the screen's for the root
    private Matrix ParentWorld(string path)
    {
        int slash = path.LastIndexOf('/');
        string? parentPath = path.Length == 0 ? null : slash < 0 ? "" : path[..slash];
        return parentPath is not null && _worlds.TryGetValue(parentPath, out Matrix parentWorld) ? parentWorld : _screenMatrix;
    }

    private static void DrawOutline(DrawingContext context, Point[] corners, IPen pen)
    {
        for (int index = 0; index < corners.Length; index++)
        {
            context.DrawLine(pen, corners[index], corners[(index + 1) % corners.Length]);
        }
    }

    private DrawItem? FindAt(Point position)
    {
        for (int index = _items.Count - 1; index >= 0; index--)
        {
            DrawItem item = _items[index];
            if (item.Transform.TryInvert(out Matrix inverse) && new Rect(item.Size).Contains(inverse.Transform(position)))
            {
                return item;
            }
        }

        return null;
    }

    private static Point[] Corners(DrawItem item)
    {
        Size size = item.Size;
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

    private sealed class Drag(string path, string clickedPath, Point start, Matrix toParent, double originX, double originY)
    {
        public string Path { get; } = path;

        public string ClickedPath { get; } = clickedPath;

        public Point Start { get; } = start;

        // from preview pixels to the space of the node's parent
        public Matrix ToParent { get; } = toParent;

        public double OriginX { get; } = originX;

        public double OriginY { get; } = originY;

        public bool IsMoving { get; set; }

        public Vector Offset { get; set; }
    }

    private sealed record DrawItem(
        SceneNode Node,
        SceneVisual Visual,
        Matrix Transform,
        Size Size,
        SceneImage? Image,
        FormattedText? Text,
        bool Hidden,
        double Alpha);
}
