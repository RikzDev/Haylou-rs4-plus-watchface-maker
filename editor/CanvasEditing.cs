using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    object? _canvasTarget;
    CanvasGesture? _canvasGesture;
    object? _pendingCanvasDrag;
    Point _pendingCanvasPoint, _pendingWindowPoint;
    readonly Dictionary<AnimationItem, int> _shownFrames = new();
    Border? _selectionOutline;
    TextBlock? _selectionCaption;
    readonly List<Thumb> _resizeHandles = new();

    sealed class CanvasGesture
    {
        public required object Target;
        public required Rect Bounds;
        public required Point Start;
        public string Mode = "move", Edge = "";
        public int X, Y, Width, Height, Spacing, Frame, OffsetX, OffsetY, PivotX, PivotY;
        public double Size, PivotFracX, PivotFracY;
        public string? Font;
        public bool FrameOnly;
        public string? SavedBin;
    }

    void AddCanvasItem(object target, UIElement visual, double x, double y)
    {
        var border = new Border { Tag = target, Child = visual, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        Panel.SetZIndex(border, LayerIndex(target));
        border.MouseLeftButtonDown += BeginDrag;
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        Overlay.Children.Add(border);
    }

    Rect CanvasBounds(object target)
    {
        if (target is Widget widget)
        {
            var visual = WidgetVisual(widget);
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Rect(widget.X, widget.Y, Math.Max(8, visual.DesiredSize.Width), Math.Max(8, visual.DesiredSize.Height));
        }
        if (target is AnimationItem animation)
        {
            var frame = _shownFrames.GetValueOrDefault(animation, 0);
            var offset = animation.OffsetAt(frame);
            return new Rect(animation.X + offset.X, animation.Y + offset.Y, Math.Max(1, animation.Width), Math.Max(1, animation.Height));
        }
        var hand = (HandItem)target;
        var local = HandBounds(hand);
        local.Offset(hand.PosX, hand.PosY);
        return local;
    }

    static Rect HandBounds(HandItem hand) => new RotateTransform(hand.PreviewDegrees,
        hand.Width * hand.PivotFracX, hand.Height * hand.PivotFracY)
        .TransformBounds(new Rect(0, 0, hand.Width, hand.Height));

    void UpdateCanvasSelection()
    {
        RefreshLayers();
        RefreshStudioSelection();
        var exists = _canvasTarget switch
        {
            Widget widget => _widgets.Contains(widget),
            AnimationItem animation => _animations.Contains(animation),
            HandItem hand => _hands.Contains(hand),
            _ => false,
        };
        if (!exists)
        {
            ManipulationLayer.Children.Clear();
            _resizeHandles.Clear();
            _selectionOutline = null;
            return;
        }
        if (_selectionOutline == null)
        {
            ManipulationLayer.Children.Clear();
            _selectionOutline = new Border { BorderBrush = (Brush)FindResource("Accent"), BorderThickness = new Thickness(1), IsHitTestVisible = false };
            ManipulationLayer.Children.Add(_selectionOutline);
            _selectionCaption = new TextBlock { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 27, 25, 22)), FontSize = 9, IsHitTestVisible = false, Padding = new Thickness(3, 1, 3, 1) };
            ManipulationLayer.Children.Add(_selectionCaption);
            foreach (var edge in new[] { "NW", "N", "NE", "W", "E", "SW", "S", "SE" })
            {
                var handle = new Thumb { Width = 10, Height = 10, Tag = edge,
                    Background = (Brush)FindResource("Accent"), BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                    Cursor = edge is "N" or "S" ? Cursors.SizeNS : edge is "W" or "E" ? Cursors.SizeWE : edge is "NW" or "SE" ? Cursors.SizeNWSE : Cursors.SizeNESW };
                var factory = new FrameworkElementFactory(typeof(Border));
                factory.SetValue(Border.BackgroundProperty, handle.Background);
                factory.SetValue(Border.BorderBrushProperty, Brushes.White);
                factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                handle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
                handle.DragStarted += (_, _) => StartCanvasGesture(_canvasTarget!, Mouse.GetPosition(Overlay), "resize", edge);
                handle.DragDelta += (_, _) => ApplyCanvasResize(Mouse.GetPosition(Overlay), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                handle.DragCompleted += (_, e) => EndCanvasGesture(e.Canceled);
                _resizeHandles.Add(handle);
                ManipulationLayer.Children.Add(handle);
            }
        }
        var bounds = CanvasBounds(_canvasTarget!);
        Canvas.SetLeft(_selectionOutline, bounds.X);
        Canvas.SetTop(_selectionOutline, bounds.Y);
        _selectionOutline.Width = bounds.Width;
        _selectionOutline.Height = bounds.Height;
        _selectionCaption!.Text = _canvasTarget switch
        {
            Widget widget => $"{widget.Label} · {bounds.Width:F0} × {bounds.Height:F0}",
            AnimationItem image => image == _selectedAnimation && _animationPreviewMode == "frame"
                ? $"{image.Label} · move this frame; resize the sequence" : $"{image.Label} · {bounds.Width:F0} × {bounds.Height:F0}",
            _ => "Clock hand · proportions preserved",
        };
        Canvas.SetLeft(_selectionCaption, Math.Clamp(bounds.X, 0, ScreenWidth - 100));
        Canvas.SetTop(_selectionCaption, bounds.Y > 18 ? bounds.Y - 18 : bounds.Bottom + 6);
        foreach (var handle in _resizeHandles)
        {
            var edge = (string)handle.Tag;
            Canvas.SetLeft(handle, (edge.Contains('W') ? bounds.Left : edge.Contains('E') ? bounds.Right : bounds.Left + bounds.Width / 2) - 5);
            Canvas.SetTop(handle, (edge.Contains('N') ? bounds.Top : edge.Contains('S') ? bounds.Bottom : bounds.Top + bounds.Height / 2) - 5);
        }
    }

    void StartCanvasGesture(object target, Point point, string mode, string edge)
    {
        if (_canvasGesture != null)
            return;
        var frameOnly = target == _selectedAnimation && _animationPreviewMode == "frame";
        var bounds = CanvasBounds(target);
        if (target is Widget widget) Select(widget);
        else if (target is AnimationItem animation) ShowAnimation(animation);
        _canvasTarget = target;
        var gesture = new CanvasGesture { Target = target, Bounds = bounds, Start = point, Mode = mode, Edge = edge,
            FrameOnly = frameOnly, SavedBin = _savedBin };
        switch (target)
        {
            case Widget text:
                gesture.X = text.X; gesture.Y = text.Y; gesture.Size = text.Size;
                gesture.Spacing = text.CharacterSpacing; gesture.Font = text.FontFile;
                break;
            case AnimationItem image:
                gesture.X = image.X; gesture.Y = image.Y; gesture.Width = image.Width; gesture.Height = image.Height;
                gesture.Frame = _shownFrames.GetValueOrDefault(image, 0);
                var offset = image.OffsetAt(gesture.Frame);
                gesture.OffsetX = offset.X; gesture.OffsetY = offset.Y;
                break;
            case HandItem hand:
                gesture.X = hand.PosX; gesture.Y = hand.PosY; gesture.Width = hand.Width; gesture.Height = hand.Height;
                gesture.PivotX = hand.PivotX; gesture.PivotY = hand.PivotY;
                gesture.PivotFracX = hand.PivotFracX; gesture.PivotFracY = hand.PivotFracY;
                break;
        }
        _canvasGesture = gesture;
        UpdateCanvasSelection();
    }

    void ApplyCanvasMove(Point point)
    {
        if (_canvasGesture is not { Mode: "move" } gesture) return;
        var delta = point - gesture.Start;
        var x = Math.Clamp(Math.Round(gesture.Bounds.X + delta.X), 0, ScreenWidth - 8);
        var y = Math.Clamp(Math.Round(gesture.Bounds.Y + delta.Y), 0, ScreenHeight - 8);
        SetGesturePosition(gesture, x - gesture.Bounds.X, y - gesture.Bounds.Y);
        GestureUpdated();
    }

    static Rect ResizeCanvasRect(Rect original, Vector delta, string edge, bool keepAspect)
    {
        var width = Math.Max(8, original.Width + (edge.Contains('E') ? delta.X : edge.Contains('W') ? -delta.X : 0));
        var height = Math.Max(8, original.Height + (edge.Contains('S') ? delta.Y : edge.Contains('N') ? -delta.Y : 0));
        if (keepAspect)
        {
            var sx = width / original.Width;
            var sy = height / original.Height;
            var horizontal = edge.Contains('E') || edge.Contains('W');
            var vertical = edge.Contains('N') || edge.Contains('S');
            var scale = !vertical ? sx : !horizontal ? sy : Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
            scale = Math.Clamp(scale, Math.Max(8 / original.Width, 8 / original.Height),
                Math.Min(ScreenWidth / original.Width, ScreenHeight / original.Height));
            width = original.Width * scale; height = original.Height * scale;
        }
        return AnchorCanvasRect(original, Math.Clamp(width, 1, ScreenWidth), Math.Clamp(height, 1, ScreenHeight), edge);
    }

    static Rect AnchorCanvasRect(Rect original, double width, double height, string edge)
    {
        var x = edge.Contains('W') ? original.Right - width : edge.Contains('E') ? original.Left : original.Left + (original.Width - width) / 2;
        var y = edge.Contains('N') ? original.Bottom - height : edge.Contains('S') ? original.Top : original.Top + (original.Height - height) / 2;
        return new Rect(Math.Clamp(x, 0, ScreenWidth - 8), Math.Clamp(y, 0, ScreenHeight - 8), width, height);
    }

    void ApplyCanvasResize(Point point, bool keepAspect)
    {
        if (_canvasGesture is not { Mode: "resize" } gesture) return;
        var rect = ResizeCanvasRect(gesture.Bounds, point - gesture.Start, gesture.Edge,
            keepAspect || gesture.Target is Widget or HandItem);
        if (gesture.Target is Widget widget)
        {
            if (!EnsureResizableFont(widget)) return;
            widget.Size = Math.Clamp(gesture.Size * rect.Height / gesture.Bounds.Height, 8, 120);
            widget.CharacterSpacing = Math.Clamp((int)Math.Round(gesture.Spacing * widget.Size / gesture.Size), -120, 120);
            if (widget.CharacterPreviews.Count > 0)
            {
                var largest = widget.CharacterPreviews.Values.Max(image => Math.Max(image.PixelWidth, image.PixelHeight));
                var cellWidth = widget.CharacterPreviews.Values.Max(image => Math.Max(1,
                    (int)Math.Floor(image.PixelWidth * widget.Size / largest + 0.5)));
                widget.CharacterSpacing = Math.Clamp(widget.CharacterSpacing, 1 - cellWidth, ScreenWidth - cellWidth);
            }
            var actual = CanvasBounds(widget);
            rect = AnchorCanvasRect(gesture.Bounds, actual.Width, actual.Height, gesture.Edge);
        }
        else if (gesture.Target is AnimationItem image)
        {
            image.Width = (int)Math.Round(rect.Width); image.Height = (int)Math.Round(rect.Height);
        }
        else if (gesture.Target is HandItem hand)
        {
            var scale = Math.Min(rect.Width / gesture.Bounds.Width,
                Math.Min(ScreenWidth / (double)gesture.Width, ScreenHeight / (double)gesture.Height));
            hand.Width = Math.Max(1, (int)Math.Round(gesture.Width * scale));
            hand.Height = Math.Max(1, (int)Math.Round(gesture.Height * scale));
            hand.PivotX = (int)Math.Round(hand.Width * hand.PivotFracX);
            hand.PivotY = (int)Math.Round(hand.Height * hand.PivotFracY);
            var actual = HandBounds(hand);
            rect = AnchorCanvasRect(gesture.Bounds, actual.Width, actual.Height, gesture.Edge);
        }
        SetGesturePosition(gesture, rect.X - gesture.Bounds.X, rect.Y - gesture.Bounds.Y);
        GestureUpdated();
    }

    void SetGesturePosition(CanvasGesture gesture, double dx, double dy)
    {
        switch (gesture.Target)
        {
            case Widget widget: widget.X = (int)Math.Round(gesture.X + dx); widget.Y = (int)Math.Round(gesture.Y + dy); break;
            case AnimationItem animation when gesture.FrameOnly:
                var offset = animation.OffsetAt(gesture.Frame);
                offset.X = (int)Math.Clamp(Math.Round(gesture.OffsetX + dx), -ScreenWidth, ScreenWidth);
                offset.Y = (int)Math.Clamp(Math.Round(gesture.OffsetY + dy), -ScreenHeight, ScreenHeight);
                break;
            case AnimationItem animation:
                animation.X = (int)Math.Round(gesture.X + dx); animation.Y = (int)Math.Round(gesture.Y + dy); break;
            case HandItem hand:
                var local = HandBounds(hand);
                hand.PosX = (int)Math.Round(gesture.Bounds.X + dx - local.X);
                hand.PosY = (int)Math.Round(gesture.Bounds.Y + dy - local.Y); break;
        }
    }

    bool EnsureResizableFont(Widget widget)
    {
        if (widget.CharacterImages.Count > 0 || !string.IsNullOrEmpty(widget.FontFile)) return true;
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
        if (!File.Exists(font)) { Log("Consolas font not found. This text cannot be resized.", LogLevel.Error); return false; }
        widget.FontFile = font;
        return true;
    }

    void GestureUpdated()
    {
        var gesture = _canvasGesture!;
        _savedBin = null;
        foreach (var border in Overlay.Children.OfType<Border>())
        {
            if (border.Tag != gesture.Target) continue;
            if (gesture.Target is Widget widget)
            {
                border.Child = WidgetVisual(widget); Canvas.SetLeft(border, widget.X); Canvas.SetTop(border, widget.Y);
                ShowSelection();
            }
            else if (gesture.Target is AnimationItem animation && border.Child is Image image)
            {
                image.Width = animation.Width; image.Height = animation.Height;
                var offset = animation.OffsetAt(gesture.Frame);
                Canvas.SetLeft(border, animation.X + offset.X); Canvas.SetTop(border, animation.Y + offset.Y);
                _filling = true;
                AnimationXBox.Text = animation.X.ToString(); AnimationYBox.Text = animation.Y.ToString();
                AnimationWidthBox.Text = animation.Width.ToString(); AnimationHeightBox.Text = animation.Height.ToString();
                FrameOffsetXBox.Text = offset.X.ToString(); FrameOffsetYBox.Text = offset.Y.ToString();
                _filling = false;
            }
            else if (gesture.Target is HandItem hand && border.Child is Image handImage)
            {
                handImage.Width = hand.Width; handImage.Height = hand.Height;
                Canvas.SetLeft(border, hand.PosX); Canvas.SetTop(border, hand.PosY);
            }
        }
        UpdateCanvasSelection();
        QueueGlyphPreview();
    }

    void EndCanvasGesture(bool cancel)
    {
        if (_canvasGesture is not { } gesture) return;
        if (cancel)
        {
            switch (gesture.Target)
            {
                case Widget widget:
                    widget.X = gesture.X; widget.Y = gesture.Y; widget.Size = gesture.Size;
                    widget.CharacterSpacing = gesture.Spacing; widget.FontFile = gesture.Font; break;
                case AnimationItem animation:
                    animation.X = gesture.X; animation.Y = gesture.Y; animation.Width = gesture.Width; animation.Height = gesture.Height;
                    var offset = animation.OffsetAt(gesture.Frame); offset.X = gesture.OffsetX; offset.Y = gesture.OffsetY; break;
                case HandItem hand:
                    hand.PosX = gesture.X; hand.PosY = gesture.Y; hand.Width = gesture.Width; hand.Height = gesture.Height;
                    hand.PivotX = gesture.PivotX; hand.PivotY = gesture.PivotY;
                    hand.PivotFracX = gesture.PivotFracX; hand.PivotFracY = gesture.PivotFracY; break;
            }
            _savedBin = gesture.SavedBin;
        }
        _canvasGesture = null;
        if (Mouse.Captured == Overlay) Mouse.Capture(null);
        if (gesture.Target is Widget) ShowSelection();
        else if (gesture.Target is AnimationItem animation) ShowAnimation(animation);
        Redraw();
    }

    void CanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pendingCanvasDrag = null;
            if (_canvasGesture is { Mode: "move" }) EndCanvasGesture(false);
            return;
        }
        if (_pendingCanvasDrag is { } target && ShouldStartCanvasDrag(e.GetPosition(this) - _pendingWindowPoint))
        {
            _pendingCanvasDrag = null;
            StartCanvasGesture(target, _pendingCanvasPoint, "move", "");
            Mouse.Capture(Overlay, CaptureMode.Element);
        }
        ApplyCanvasMove(e.GetPosition(Overlay));
    }
    static bool ShouldStartCanvasDrag(Vector delta) =>
        Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;

    void CanvasWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pendingCanvasDrag = null;
        if (_canvasGesture is { Mode: "move" }) EndCanvasGesture(false);
    }
    void CanvasWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        _pendingCanvasDrag = null;
        if (_canvasGesture is { Mode: "move" }) EndCanvasGesture(false);
    }
    void CanvasMouseLeave(object sender, MouseEventArgs e) => _pendingCanvasDrag = null;
    void CanvasWindowDeactivated(object? sender, EventArgs e)
    {
        _pendingCanvasDrag = null;
        if (_canvasGesture != null) EndCanvasGesture(true);
    }
    void CanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _pendingCanvasDrag = null;
        if (_canvasGesture is { Mode: "move" }) { EndCanvasGesture(false); e.Handled = true; }
    }
    void CanvasLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_canvasGesture is { Mode: "move" } && Mouse.Captured != Overlay) EndCanvasGesture(false);
    }
    void CanvasBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != Overlay) return;
        _canvasTarget = null; UpdateCanvasSelection();
    }
    void CanvasEscape(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _pendingCanvasDrag = null;
        if (_canvasGesture == null) return;
        EndCanvasGesture(true);
        foreach (var handle in _resizeHandles) if (handle.IsDragging) handle.CancelDrag();
        e.Handled = true;
    }
}
