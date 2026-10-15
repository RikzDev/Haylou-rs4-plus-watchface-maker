using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class IconLibraryWindow : Window
{
    readonly List<BuiltinIconChoice> _choices;
    readonly HashSet<BuiltinIconChoice> _selection = new();
    readonly string? _appendTarget;
    readonly int _existingFrames;
    bool _filtering;
    public BuiltinIconChoice? SelectedIcon { get; private set; }
    public List<BuiltinIconChoice> SelectedIcons { get; private set; } = new();
    public bool AppendToSequence { get; private set; }

    public IconLibraryWindow(List<BuiltinIconChoice> choices, string? appendTarget = null, int existingFrames = 0)
    {
        _choices = choices;
        _appendTarget = appendTarget;
        _existingFrames = existingFrames;
        InitializeComponent();
        CategoryBox.ItemsSource = new[] { "All icons" }.Concat(choices.Select(icon => icon.Category).Distinct().Order());
        CategoryBox.SelectedIndex = 0;
        FilterIcons();
    }

    void FilterChanged(object sender, TextChangedEventArgs e) => FilterIcons();
    void CategoryChanged(object sender, SelectionChangedEventArgs e) => FilterIcons();

    void FilterIcons()
    {
        if (IconList == null || CategoryBox == null || SearchBox == null) return;
        var text = SearchBox.Text.Trim();
        var category = CategoryBox.SelectedItem as string;
        var icons = _choices.Where(icon => (category == null || category == "All icons" || icon.Category == category)
            && (icon.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
                || icon.ResourceName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || icon.Category.Contains(text, StringComparison.OrdinalIgnoreCase))).ToList();
        _filtering = true;
        IconList.ItemsSource = icons;
        foreach (var icon in icons.Where(_selection.Contains)) IconList.SelectedItems.Add(icon);
        _filtering = false;
        ResultCount.Text = $"{icons.Count} of {_choices.Count} icons and sequences";
        UpdateSelection();
    }

    void IconSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_filtering) return;
        foreach (BuiltinIconChoice icon in e.RemovedItems) _selection.Remove(icon);
        foreach (BuiltinIconChoice icon in e.AddedItems) _selection.Add(icon);
        UpdateSelection();
    }

    void UpdateSelection()
    {
        var frames = _selection.Sum(icon => icon.FrameCount);
        AddIconButton.IsEnabled = frames is > 0 and <= 64;
        AddIconButton.Content = _selection.Count > 1 ? "Create sequence" : frames > 1 ? "Add sequence" : "Add icon";
        AppendIconButton.IsEnabled = _appendTarget != null && frames > 0 && frames + _existingFrames <= 64;
        SelectionInfo.Text = frames == 0 ? "Select an icon, or select several to build a sequence."
            : $"{_selection.Count} selected · {frames} frames. Library order is used; reorder frames in the editor."
                + (frames > 64 ? " Maximum: 64 frames per sequence." : "");
        AppendInfo.Text = _appendTarget == null ? "Select a sequence in the editor to add frames to it."
            : $"Add frames to: {_appendTarget} ({_existingFrames} existing frames)."
                + (frames + _existingFrames > 64 ? " This would exceed the 64-frame limit." : "");
    }

    void SelectShownClick(object sender, RoutedEventArgs e) => IconList.SelectAll();

    void ClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _selection.Clear();
        IconList.UnselectAll();
        UpdateSelection();
    }

    void AddClick(object sender, RoutedEventArgs e)
    {
        AcceptSelection(false);
    }

    void AppendClick(object sender, RoutedEventArgs e) => AcceptSelection(true);

    void AcceptSelection(bool append)
    {
        var icons = _choices.Where(_selection.Contains).ToList();
        var count = icons.Sum(icon => icon.FrameCount);
        if (count == 0 || count + (append ? _existingFrames : 0) > 64 || (append && _appendTarget == null)) return;
        SelectedIcons = icons;
        SelectedIcon = icons[0];
        AppendToSequence = append;
        DialogResult = true;
    }

    void AddDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target
            && ItemsControl.ContainerFromElement(IconList, target) is ListBoxItem) AddClick(sender, e);
    }
}

public sealed class BuiltinIconChoice
{
    public required string ResourceName { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public required BitmapSource Preview { get; init; }
    public int FrameCount { get; init; }
    public string FrameDescription => FrameCount == 1 ? "Fixed icon" : $"{FrameCount} frames";
}
