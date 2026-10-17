using System.Windows;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    void PickTextColorClick(object sender, RoutedEventArgs e)
    {
        if (_layerBusy || _selected == null || !ColorBox.IsEnabled) return;
        var target = _selected;
        var picker = new ColorPickerWindow(target.Color) { Owner = this };
        if (picker.ShowDialog() == true && ReferenceEquals(target, _selected)) ColorBox.Text = picker.SelectedHex;
    }

    void PickAnimationColorClick(object sender, RoutedEventArgs e)
    {
        if (_layerBusy || _selectedAnimation == null) return;
        var target = _selectedAnimation;
        var picker = new ColorPickerWindow(target.TintColor ?? "#FFFFFF", true) { Owner = this };
        if (picker.ShowDialog() == true && ReferenceEquals(target, _selectedAnimation)) AnimationColorBox.Text = picker.SelectedHex;
    }

    void PickBackgroundColorClick(object sender, RoutedEventArgs e)
    {
        if (_layerBusy) return;
        var picker = new ColorPickerWindow(_backgroundColor) { Owner = this };
        if (picker.ShowDialog() == true) BackgroundColorBox.Text = picker.SelectedHex;
    }
}
