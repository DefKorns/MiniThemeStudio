using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MiniThemeStudio.App.Controls;

// Lets a ScrollViewer be moved by dragging its content, as large images (atlases, sprite sheets) are looked
// around in. A press that moves less than a few pixels stays a click for the content.
public static class DragToScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(DragToScroll));

    // screen pixels the pointer moves before a press becomes a drag
    private const double Threshold = 4;

    private static readonly AttachedProperty<Drag?> DragProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Drag?>("Drag", typeof(DragToScroll));

    static DragToScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer scrollViewer) => scrollViewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer scrollViewer, bool value) => scrollViewer.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.GetNewValue<bool>())
        {
            // the content may handle the press (a click that selects); the drag is followed anyway
            scrollViewer.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            scrollViewer.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            scrollViewer.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        }
        else
        {
            scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            scrollViewer.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            scrollViewer.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer && scrollViewer.GetValue(DragProperty) is null)
        {
            scrollViewer.SetValue(DragProperty, new Drag(e.GetPosition(scrollViewer), scrollViewer.Offset));
        }
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || scrollViewer.GetValue(DragProperty) is not { } drag)
        {
            return;
        }

        Vector moved = drag.Start - e.GetPosition(scrollViewer);
        if (!drag.IsDragging && Math.Abs(moved.X) < Threshold && Math.Abs(moved.Y) < Threshold)
        {
            return;
        }

        if (!drag.IsDragging)
        {
            drag.IsDragging = true;
            e.Pointer.Capture(scrollViewer);
            scrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        scrollViewer.Offset = new Vector(
            Math.Clamp(drag.Offset.X + moved.X, 0, Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width)),
            Math.Clamp(drag.Offset.Y + moved.Y, 0, Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height)));
        e.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || scrollViewer.GetValue(DragProperty) is not { } drag)
        {
            return;
        }

        scrollViewer.SetValue(DragProperty, null);
        if (drag.IsDragging)
        {
            e.Pointer.Capture(null);
            scrollViewer.Cursor = null;
            e.Handled = true;
        }
    }

    private sealed class Drag(Point start, Vector offset)
    {
        public Point Start { get; } = start;

        public Vector Offset { get; } = offset;

        public bool IsDragging { get; set; }
    }
}
