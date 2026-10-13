using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Controls;

public sealed class AtlasView : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<AtlasView, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<IReadOnlyList<SpriteRegion>?> SpritesProperty =
        AvaloniaProperty.Register<AtlasView, IReadOnlyList<SpriteRegion>?>(nameof(Sprites));

    public static readonly StyledProperty<SpriteRegion?> SelectedSpriteProperty =
        AvaloniaProperty.Register<AtlasView, SpriteRegion?>(nameof(SelectedSprite), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<SpriteRegion?> HoveredSpriteProperty =
        AvaloniaProperty.Register<AtlasView, SpriteRegion?>(nameof(HoveredSprite));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<AtlasView, double>(nameof(Zoom), 1);

    private static readonly IBrush BackdropBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    private static readonly IPen OutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 170, 255)), 1);
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1);
    private static readonly IBrush SelectionFill = new SolidColorBrush(Color.FromArgb(70, 255, 170, 0));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 170, 0)), 2);

    static AtlasView()
    {
        AffectsRender<AtlasView>(SourceProperty, SpritesProperty, SelectedSpriteProperty, HoveredSpriteProperty, ZoomProperty);
        AffectsMeasure<AtlasView>(SourceProperty, ZoomProperty);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IReadOnlyList<SpriteRegion>? Sprites
    {
        get => GetValue(SpritesProperty);
        set => SetValue(SpritesProperty, value);
    }

    public SpriteRegion? SelectedSprite
    {
        get => GetValue(SelectedSpriteProperty);
        set => SetValue(SelectedSpriteProperty, value);
    }

    public SpriteRegion? HoveredSprite
    {
        get => GetValue(HoveredSpriteProperty);
        private set => SetValue(HoveredSpriteProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return Source is { } source
            ? new Size(source.PixelSize.Width * Zoom, source.PixelSize.Height * Zoom)
            : default;
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source)
        {
            return;
        }

        var destination = new Rect(0, 0, source.PixelSize.Width * Zoom, source.PixelSize.Height * Zoom);
        context.FillRectangle(BackdropBrush, destination);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            context.DrawImage(source, new Rect(source.Size), destination);
        }

        foreach (var sprite in Sprites ?? [])
        {
            context.DrawRectangle(null, OutlinePen, ToView(sprite).Deflate(0.5));
        }

        if (HoveredSprite is { } hovered && hovered != SelectedSprite)
        {
            context.DrawRectangle(null, HoverPen, ToView(hovered).Deflate(0.5));
        }

        if (SelectedSprite is { } selected)
        {
            context.DrawRectangle(SelectionFill, SelectionPen, ToView(selected).Deflate(1));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (FindSprite(e.GetPosition(this)) is { } sprite)
        {
            SelectedSprite = sprite;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        HoveredSprite = FindSprite(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredSprite = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedSpriteProperty && SelectedSprite is { } sprite)
        {
            RaiseEvent(new RequestBringIntoViewEventArgs
            {
                RoutedEvent = RequestBringIntoViewEvent,
                TargetObject = this,
                TargetRect = ToView(sprite).Inflate(24),
            });
        }
    }

    private SpriteRegion? FindSprite(Point position)
    {
        return Sprites is null || Zoom <= 0
            ? null
            : AtlasHitTest.FindAt(Sprites, position.X / Zoom, position.Y / Zoom);
    }

    private Rect ToView(SpriteRegion sprite)
    {
        var frame = sprite.Frame;
        return new Rect(frame.X * Zoom, frame.Y * Zoom, frame.Width * Zoom, frame.Height * Zoom);
    }
}
