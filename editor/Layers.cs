using System.Windows;
using System.Windows.Controls;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    bool _updatingLayers;
    bool _layerBusy;
    string _layerSignature = "";
    object? _shownLayerTarget;

    IEnumerable<object> LayerTargets() => _animations.Cast<object>().Concat(_hands).Concat(_widgets);

    static int LayerIndex(object target) => target switch
    {
        Widget text => text.ZIndex,
        AnimationItem image => image.ZIndex,
        HandItem hand => hand.ZIndex,
        _ => 0,
    };

    static void SetLayerIndex(object target, int value)
    {
        switch (target)
        {
            case Widget text: text.ZIndex = value; break;
            case AnimationItem image: image.ZIndex = value; break;
            case HandItem hand: hand.ZIndex = value; break;
        }
    }

    static string LayerLabel(object target) => target switch
    {
        Widget text => text.Label + " (text)",
        AnimationItem image => image.Label + (image.Frames.Count > 1 ? " (sequence)" : " (image)"),
        HandItem hand => hand.Kind + " hand",
        _ => "Element",
    };

    void RefreshLayers()
    {
        if (LayerList == null || _updatingLayers) return;
        var targets = LayerTargets().ToList();
        var target = targets.Contains(_canvasTarget!) ? _canvasTarget : null;
        var signature = string.Join("|", targets.Select(item => $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item)}:{LayerIndex(item)}:{LayerLabel(item)}"));
        _updatingLayers = true;
        try
        {
            if (_layerSignature != signature)
            {
                // For equal indices the element drawn last is the top one.
                LayerList.ItemsSource = targets.Select((item, index) => new LayerEntry(item, index))
                    .OrderByDescending(entry => LayerIndex(entry.Target)).ThenByDescending(entry => entry.Order).ToList();
                _layerSignature = signature;
            }
            LayerList.SelectedItem = LayerList.Items.Cast<LayerEntry>().FirstOrDefault(entry => ReferenceEquals(entry.Target, target));
            LayerZIndexBox.IsEnabled = BringToFrontButton.IsEnabled = SendToBackButton.IsEnabled = target != null && !_layerBusy;
            if (!ReferenceEquals(target, _shownLayerTarget) || !LayerZIndexBox.IsKeyboardFocusWithin)
                LayerZIndexBox.Text = target == null ? "" : LayerIndex(target).ToString();
            _shownLayerTarget = target;
        }
        finally { _updatingLayers = false; }
    }

    void LayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLayers || LayerList.SelectedItem is not LayerEntry entry) return;
        switch (entry.Target)
        {
            case Widget text: Select(text); break;
            case AnimationItem image: ShowAnimation(image); break;
            default: _canvasTarget = entry.Target; break;
        }
        Redraw();
    }

    void LayerZIndexChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingLayers || _layerBusy || _canvasTarget == null || !LayerTargets().Contains(_canvasTarget)
            || !int.TryParse(LayerZIndexBox.Text, out var value)) return;
        SetLayerIndex(_canvasTarget, value);
        _savedBin = null;
        Redraw();
    }

    void MoveLayerToEdge(bool front)
    {
        var targets = LayerTargets().ToList();
        if (_canvasTarget == null || !targets.Contains(_canvasTarget)) return;
        var edge = front ? targets.Max(LayerIndex) : targets.Min(LayerIndex);
        if ((front && edge == int.MaxValue) || (!front && edge == int.MinValue))
        {
            var ordered = targets.OrderBy(LayerIndex).ToList();
            for (var index = 0; index < ordered.Count; index++) SetLayerIndex(ordered[index], index);
            edge = front ? ordered.Count - 1 : 0;
        }
        SetLayerIndex(_canvasTarget, front ? edge + 1 : edge - 1);
        _savedBin = null;
        Redraw();
    }

    void BringToFrontClick(object sender, RoutedEventArgs e) => MoveLayerToEdge(true);
    void SendToBackClick(object sender, RoutedEventArgs e) => MoveLayerToEdge(false);

    sealed record LayerEntry(object Target, int Order)
    {
        public string Display => $"{LayerIndex(Target)} · {LayerLabel(Target)}";
    }
}
