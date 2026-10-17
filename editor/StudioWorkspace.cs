using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    bool _studioReady, _syncingStudio, _fitArtboard = true, _scrubbingTimeline;
    double _artboardZoom = 1, _timelineZoom = 100;
    string _studioTracksSignature = "", _studioTimelineSignature = "";
    object? _studioInspectorTarget;
    Point _frameDragStart;
    int _frameDragIndex = -1;
    AnimationItem? _frameDragOwner;
    int _lastTimelinePreviewIndex = -1;

    double TimelineFrameWidth => Math.Round(96 * _timelineZoom / 100);
    static int EffectiveFrameDuration(AnimationItem animation) => Math.Max(1, (animation.Interval + 199) / 200) * 200;

    void InitializeStudioWorkspace()
    {
        _studioReady = true;
        InspectorTabs.SelectedIndex = 2;
        NameBox.TextChanged += (_, _) => RefreshStudioDocumentTitle();
        TitleBox.TextChanged += (_, _) => RefreshStudioDocumentTitle();
        Loaded += (_, _) => { UpdateArtboardZoom(); RefreshStudioSelection(); };
        RefreshStudioDocumentTitle();
        RefreshStudioSelection();
    }

    void RefreshStudioDocumentTitle()
    {
        if (!_studioReady) return;
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled" : NameBox.Text.Trim();
        StudioDocumentTitle.Text = name + " / Design";
        Title = name + " - RS4 Studio";
    }

    void RefreshStudioSelection()
    {
        if (!_studioReady || _syncingStudio) return;
        _syncingStudio = true;
        try
        {
            if (!ReferenceEquals(_studioInspectorTarget, _canvasTarget))
            {
                _studioInspectorTarget = _canvasTarget;
                if (_canvasTarget is Widget) InspectorTabs.SelectedIndex = 0;
                else if (_canvasTarget is AnimationItem) InspectorTabs.SelectedIndex = 1;
            }
            StudioInspectorType.Text = _canvasTarget switch
            {
                Widget => "Text", AnimationItem a => a.Frames.Count > 1 ? "Sequence" : "Image",
                HandItem => "Clock hand", _ => InspectorTabs.SelectedIndex == 2 ? "Background" : "No selection",
            };
            TextPropertiesPanel.IsEnabled = _selected != null && !_layerBusy;
            ImagePropertiesPanel.IsEnabled = _selectedAnimation != null && !_layerBusy;
            RemoveButton.IsEnabled = _selected != null && !_layerBusy;
            RemoveAnimationButton.IsEnabled = _selectedAnimation != null && !_layerBusy;
            var signature = string.Join("|", _animations.Select(a => $"{a.AssetKey}:{a.Label}:{a.Frames.Count}:{a.Source}:{a.Interval}"));
            if (_studioTracksSignature != signature)
            {
                _studioTracksSignature = signature;
                TimelineTrackList.ItemsSource = _animations.Select(a => new StudioTrack(a,
                    $"{a.Frames.Count} frame{(a.Frames.Count == 1 ? "" : "s")} / {AnimationChoices.FirstOrDefault(c => c.Key == a.Source)?.Label ?? a.Source}")).ToList();
            }
            var track = TimelineTrackList.Items.Cast<StudioTrack>().FirstOrDefault(t => ReferenceEquals(t.Animation, _selectedAnimation));
            if (!ReferenceEquals(TimelineTrackList.SelectedItem, track))
            {
                TimelineTrackList.SelectedItem = track;
                if (track != null)
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                    {
                        if (ReferenceEquals(TimelineTrackList.SelectedItem, track)) TimelineTrackList.ScrollIntoView(track);
                    });
            }
            StudioStatus.Text = _layerBusy ? "Working / See Output for details" : _canvasTarget == null ? "Ready / Select an element or add one from the toolbar"
                : LayerLabel(_canvasTarget) + " / " + (InspectorTabs.SelectedIndex == 0 ? "Text properties" : "Design");
        }
        finally { _syncingStudio = false; }
        RefreshStudioTimeline();
    }

    void InspectorTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_studioReady || _syncingStudio || !ReferenceEquals(e.Source, InspectorTabs)) return;
        if (InspectorTabs.SelectedIndex == 0 && _selected != null) Select(_selected);
        else if (InspectorTabs.SelectedIndex == 1 && _selectedAnimation != null) ShowAnimation(_selectedAnimation);
        else if (InspectorTabs.SelectedIndex == 2) _canvasTarget = null;
        Redraw();
    }

    void ShowExplorerClick(object sender, RoutedEventArgs e) => ExplorerTabs.SelectedIndex = 0;
    void ShowAssetsClick(object sender, RoutedEventArgs e) => ExplorerTabs.SelectedIndex = 1;
    void ShowTimelineClick(object sender, RoutedEventArgs e) => StudioBottomTabs.SelectedIndex = 0;
    void ShowOutputClick(object sender, RoutedEventArgs e) => StudioBottomTabs.SelectedIndex = 1;
    void ClearOutputClick(object sender, RoutedEventArgs e) => LogBox.Document.Blocks.Clear();

    void SelectBackgroundClick(object sender, RoutedEventArgs e)
    {
        _canvasTarget = null;
        InspectorTabs.SelectedIndex = 2;
        Redraw();
    }

    void ResetStudioLayoutClick(object sender, RoutedEventArgs e)
    {
        ExplorerColumn.Width = new GridLength(244);
        InspectorColumn.Width = new GridLength(328);
        TimelineRow.Height = new GridLength(270);
        WorkspaceGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        WorkspaceGrid.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
        FitArtboardClick(sender, e);
    }

    void FitArtboardClick(object sender, RoutedEventArgs e) { _fitArtboard = true; UpdateArtboardZoom(); }
    void ZoomInClick(object sender, RoutedEventArgs e) => SetArtboardZoom(_artboardZoom * 1.2);
    void ZoomOutClick(object sender, RoutedEventArgs e) => SetArtboardZoom(_artboardZoom / 1.2);
    void ArtboardViewportChanged(object sender, SizeChangedEventArgs e) { if (_studioReady) UpdateArtboardZoom(); }
    void ArtboardMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetArtboardZoom(_artboardZoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }
    void SetArtboardZoom(double zoom) { _fitArtboard = false; _artboardZoom = Math.Clamp(zoom, 0.25, 4); UpdateArtboardZoom(); }
    void UpdateArtboardZoom()
    {
        if (!_studioReady) return;
        if (_fitArtboard && ArtboardScroll.ActualWidth > 64 && ArtboardScroll.ActualHeight > 64)
            _artboardZoom = Math.Clamp(Math.Min((ArtboardScroll.ActualWidth - 64) / ScreenWidth,
                (ArtboardScroll.ActualHeight - 64) / ScreenHeight), 0.1, 4);
        WatchViewbox.Width = ScreenWidth * _artboardZoom;
        WatchViewbox.Height = ScreenHeight * _artboardZoom;
        StudioZoomLabel.Text = $"{_artboardZoom:P0}";
        StudioZoomLabel.ToolTip = _fitArtboard ? "Fit artboard" : "Manual zoom / Ctrl+mouse wheel";
    }

    void TimelineTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_studioReady || _syncingStudio || TimelineTrackList.SelectedItem is not StudioTrack track) return;
        ShowAnimation(track.Animation);
        Redraw();
    }

    void RefreshStudioTimeline(bool rebuild = false)
    {
        if (!_studioReady) return;
        var animation = _selectedAnimation;
        var count = animation?.Frames.Count ?? 0;
        var signature = $"{animation?.AssetKey}:{count}:{animation?.Interval}:{animation?.Source}:{_timelineZoom}:{_animationPreviewMode}";
        if (rebuild || signature != _studioTimelineSignature)
        {
            _studioTimelineSignature = signature;
            _lastTimelinePreviewIndex = -1;
            var width = count * TimelineFrameWidth;
            TimelineFilmstrip.Width = Math.Max(1, width);
            TimelineRuler.Width = Math.Max(1, width);
            TimelineRuler.Children.Clear();
            if (animation != null)
            {
                bool timed = animation.Source == "loop" || _animationPreviewMode == "loop";
                for (int index = 0; index < count; index++)
                {
                    var x = index * TimelineFrameWidth;
                    var tick = new System.Windows.Shapes.Line { X1 = x + 1, X2 = x + 1, Y1 = 17, Y2 = 26, Stroke = (Brush)FindResource("Line") };
                    TimelineRuler.Children.Add(tick);
                    var label = new TextBlock { Text = timed ? (index * EffectiveFrameDuration(animation) / 1000.0).ToString("0.0's'", CultureInfo.InvariantCulture)
                        : "Frame " + (index + 1).ToString("D2"), Foreground = (Brush)FindResource("Muted"), FontSize = 10, IsHitTestVisible = false };
                    Canvas.SetLeft(label, x + 6); Canvas.SetTop(label, 3);
                    TimelineRuler.Children.Add(label);
                }
            }
            TimelineEmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TimelineCursorCanvas.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        bool available = count > 0 && !_layerBusy;
        bool editableFrames = available && animation?.Source != "weather";
        TimelineFirstButton.IsEnabled = TimelinePreviousButton.IsEnabled = TimelineNextButton.IsEnabled = available;
        AnimationPlayButton.IsEnabled = TimelineRestartButton.IsEnabled = available && count > 1;
        AnimationPreviewModeBox.IsEnabled = AnimationFrameSlider.IsEnabled = TimelineScaleSlider.IsEnabled = available;
        FrameOffsetXBox.IsEnabled = FrameOffsetYBox.IsEnabled = available && _selectedFrameIndex >= 0;
        AddFrameButton.IsEnabled = AddLibraryFramesButton.IsEnabled = editableFrames;
        RemoveFrameButton.IsEnabled = editableFrames && count > 1 && _selectedFrameIndex >= 0;
        MoveFrameUpButton.IsEnabled = editableFrames && _selectedFrameIndex > 0;
        MoveFrameDownButton.IsEnabled = editableFrames && _selectedFrameIndex >= 0 && _selectedFrameIndex < count - 1;
        UpdateTimelineCursor();
    }

    void UpdateTimelineCursor()
    {
        if (!_studioReady || _selectedAnimation is not { Frames.Count: > 0 } animation)
        {
            if (_studioReady) { TimelineClock.Text = "00:00.000"; AnimationPlayButton.Content = "Play"; }
            return;
        }
        int index = Math.Clamp(PreviewFrameIndex(animation), 0, animation.Frames.Count - 1);
        TimelineCursor.X1 = TimelineCursor.X2 = index * TimelineFrameWidth + 2;
        TimelineCursor.Y2 = Math.Max(110, TimelineFilmstrip.ActualHeight);
        if (index != _lastTimelinePreviewIndex && TimelineFrameScroller.ViewportWidth > 0)
        {
            var x = index * TimelineFrameWidth;
            if (x < TimelineFrameScroller.HorizontalOffset || x + TimelineFrameWidth > TimelineFrameScroller.HorizontalOffset + TimelineFrameScroller.ViewportWidth)
                TimelineFrameScroller.ScrollToHorizontalOffset(Math.Max(0, x - TimelineFrameScroller.ViewportWidth / 3));
            _lastTimelinePreviewIndex = index;
        }
        bool timed = animation.Source == "loop" || _animationPreviewMode == "loop";
        int milliseconds = index * EffectiveFrameDuration(animation);
        TimelineClock.Text = timed ? $"{milliseconds / 60000:D2}:{milliseconds / 1000 % 60:D2}.{milliseconds % 1000:D3}" : "FRAME " + (index + 1).ToString("D2");
        AnimationFrameInfo.Text = _animationPreviewMode switch
        {
            "loop" => $"{(_animationPreviewPlaying ? "Playing" : "Paused")} / {index + 1} of {animation.Frames.Count}",
            "frame" => $"Single frame / {index + 1} of {animation.Frames.Count}",
            _ => $"Live data / {index + 1} of {animation.Frames.Count}",
        };
        TimelineClock.ToolTip = timed ? "Loop inspection time" : "Value-based selection; timing applies only to loop inspection";
        AnimationPlayButton.Content = _animationPreviewMode == "loop" && _animationPreviewPlaying ? "Pause" : "Play";
    }

    void TimelineScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _timelineZoom = e.NewValue;
        if (!_studioReady) return;
        RefreshFrames(_selectedFrameIndex);
    }

    void FirstTimelineFrameClick(object sender, RoutedEventArgs e) => SetPreviewFrame(0, true);
    void PreviousTimelineFrameClick(object sender, RoutedEventArgs e) => StepTimeline(-1);
    void NextTimelineFrameClick(object sender, RoutedEventArgs e) => StepTimeline(1);
    void StepTimeline(int delta)
    {
        if (_selectedAnimation == null || _layerBusy) return;
        SetPreviewFrame(PreviewFrameIndex(_selectedAnimation) + delta, true);
        FrameList.ScrollIntoView(FrameList.SelectedItem);
    }
    void RestartTimelineClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAnimation == null || _layerBusy) return;
        _animationTicks = 0;
        _animationPreviewMode = "loop";
        _animationPreviewPlaying = true;
        _filling = true; AnimationPreviewModeBox.SelectedIndex = 1; _filling = false;
        UpdatePreviewControls(); Redraw();
    }

    void ScrubTimeline(double x)
    {
        if (_selectedAnimation == null || _layerBusy) return;
        SetPreviewFrame((int)Math.Floor(x / TimelineFrameWidth), true);
    }
    void TimelineRulerDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedAnimation == null || _layerBusy) return;
        _scrubbingTimeline = true; TimelineRuler.CaptureMouse(); ScrubTimeline(e.GetPosition(TimelineRuler).X); e.Handled = true;
    }
    void TimelineRulerMove(object sender, MouseEventArgs e)
    {
        if (_scrubbingTimeline && e.LeftButton == MouseButtonState.Pressed) { ScrubTimeline(e.GetPosition(TimelineRuler).X); e.Handled = true; }
    }
    void TimelineRulerUp(object sender, MouseButtonEventArgs e) { _scrubbingTimeline = false; TimelineRuler.ReleaseMouseCapture(); e.Handled = true; }
    void TimelineRulerLostCapture(object sender, MouseEventArgs e) => _scrubbingTimeline = false;

    void TimelineFrameMouseDown(object sender, MouseButtonEventArgs e)
    {
        _frameDragStart = e.GetPosition(FrameList);
        _frameDragIndex = FrameContainer(e.OriginalSource as DependencyObject) is { } item ? FrameList.Items.IndexOf(item.DataContext) : -1;
        _frameDragOwner = _selectedAnimation;
    }
    ListBoxItem? FrameContainer(DependencyObject? source) => source == null ? null : ItemsControl.ContainerFromElement(FrameList, source) as ListBoxItem;
    void TimelineFrameMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _frameDragIndex < 0 || _layerBusy || _selectedAnimation?.Source == "weather" || !ReferenceEquals(_frameDragOwner, _selectedAnimation)) return;
        var delta = e.GetPosition(FrameList) - _frameDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var payload = new StudioFrameDrag(_frameDragOwner!, _frameDragIndex);
        _frameDragIndex = -1;
        DragDrop.DoDragDrop(FrameList, new DataObject("Haylou.Studio.Frame", payload), DragDropEffects.Move);
        e.Handled = true;
    }
    void TimelineFrameDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_layerBusy && _selectedAnimation?.Source != "weather" && e.Data.GetData("Haylou.Studio.Frame") is StudioFrameDrag drag && ReferenceEquals(drag.Animation, _selectedAnimation)
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    void TimelineFrameDrop(object sender, DragEventArgs e)
    {
        if (!_layerBusy && e.Data.GetData("Haylou.Studio.Frame") is StudioFrameDrag drag && ReferenceEquals(drag.Animation, _selectedAnimation))
        {
            var container = FrameContainer(e.OriginalSource as DependencyObject);
            var target = container == null ? drag.Animation.Frames.Count - 1 : FrameList.Items.IndexOf(container.DataContext);
            ReorderTimelineFrame(drag.Index, target);
        }
        e.Handled = true;
    }
    void ReorderTimelineFrame(int from, int to)
    {
        var animation = _selectedAnimation;
        if (_layerBusy || animation == null || animation.Source == "weather" || from < 0 || to < 0 || from >= animation.Frames.Count || to >= animation.Frames.Count || from == to) return;
        animation.OffsetAt(animation.Frames.Count - 1);
        static void Move<T>(List<T> list, int from, int to) { var item = list[from]; list.RemoveAt(from); list.Insert(to, item); }
        Move(animation.Frames, from, to); Move(animation.Previews, from, to); Move(animation.FrameOffsets, from, to);
        if (animation.OriginalFrames.Count > 0) Move(animation.OriginalFrames, from, to);
        _savedBin = null;
        RefreshFrames(to); SetPreviewFrame(to, true); Redraw();
    }

    void SetStudioBusy(bool busy)
    {
        if (!_studioReady) return;
        StudioMenu.IsEnabled = StudioToolbar.IsEnabled = ExplorerTabs.IsEnabled = InspectorTabs.IsEnabled = StudioTimeline.IsEnabled = Watch.IsEnabled = !busy;
        if (busy) { StudioBottomTabs.SelectedIndex = 1; StudioStatus.Text = "Working / See Output for details"; }
        else RefreshStudioSelection();
    }

    void StudioKeyDown(object sender, KeyEventArgs e)
    {
        CanvasEscape(sender, e);
        if (e.Handled || !_studioReady || _layerBusy) return;
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (control)
        {
            if (e.Key == Key.O) OpenClick(sender, e);
            else if (e.Key == Key.I) ImportClick(sender, e);
            else if (e.Key == Key.S) SaveProjectClick(sender, e);
            else if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) SaveBinClick(sender, e);
            else return;
            e.Handled = true; return;
        }
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or Slider) return;
        if (_selectedAnimation == null || StudioBottomTabs.SelectedIndex != 0) return;
        if (e.Key == Key.Space) AnimationPlayClick(sender, e);
        else if (e.Key == Key.Left) StepTimeline(-1);
        else if (e.Key == Key.Right) StepTimeline(1);
        else if (e.Key == Key.Home) SetPreviewFrame(0, true);
        else if (e.Key == Key.End) SetPreviewFrame(_selectedAnimation.Frames.Count - 1, true);
        else return;
        e.Handled = true;
    }

    sealed record StudioTrack(AnimationItem Animation, string Detail) { public string Label => Animation.Label; }
    sealed record StudioFrameDrag(AnimationItem Animation, int Index);
}
