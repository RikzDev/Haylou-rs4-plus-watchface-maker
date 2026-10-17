using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HaylouFaceEditor;

public partial class ColorPickerWindow : Window
{
    readonly bool _allowAlpha;
    bool _syncing = true;
    double _hue, _saturation, _value;
    byte _alpha = 255;
    Color _color;
    public string SelectedHex => FormatHex(_color, _allowAlpha);

    public ColorPickerWindow(string initialHex, bool allowAlpha = false)
    {
        _allowAlpha = allowAlpha;
        InitializeComponent();
        OpacityPanel.Visibility = allowAlpha ? Visibility.Visible : Visibility.Collapsed;
        HexLabel.Text = allowAlpha ? "HEX / #RRGGBB or #AARRGGBB" : "HEX / #RRGGBB";
        HexBox.ToolTip = allowAlpha ? "Six RGB digits or eight ARGB digits, with optional #" : "Six RGB digits, with optional #";
        if (!TryParseHex(initialHex, allowAlpha, out var color)) color = Colors.White;
        CurrentPreview.Background = new SolidColorBrush(color);
        SetColor(color);
    }

    internal static bool TryParseHex(string text, bool allowAlpha, out Color color)
    {
        color = default;
        var digits = text.Trim();
        if (digits.StartsWith('#')) digits = digits[1..];
        if (digits.Length != 6 && !(allowAlpha && digits.Length == 8)) return false;
        if (!uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
        color = Color.FromArgb(digits.Length == 8 ? (byte)(value >> 24) : (byte)255,
            (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    static string FormatHex(Color color, bool alpha) => alpha && color.A != 255
        ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    void SetColor(Color color, bool updateHex = true)
    {
        _color = color;
        _alpha = _allowAlpha ? color.A : (byte)255;
        var r = color.R / 255.0; var g = color.G / 255.0; var b = color.B / 255.0;
        var maximum = Math.Max(r, Math.Max(g, b)); var minimum = Math.Min(r, Math.Min(g, b));
        var delta = maximum - minimum;
        _value = maximum;
        _saturation = maximum == 0 ? 0 : delta / maximum;
        // Keep the selected hue for grayscale, so raising saturation stays useful.
        if (delta > 0)
        {
            _hue = 60 * (maximum == r ? ((g - b) / delta) % 6
                : maximum == g ? (b - r) / delta + 2 : (r - g) / delta + 4);
            if (_hue < 0) _hue += 360;
        }
        RefreshColor(updateHex);
    }

    internal static Color FromHsv(double hue, double saturation, double value, byte alpha = 255)
    {
        hue = ((hue % 360) + 360) % 360;
        var c = value * saturation; var x = c * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
        return Color.FromArgb(alpha, Channel(r + m), Channel(g + m), Channel(b + m));
    }

    void RefreshColor(bool updateHex = true)
    {
        _syncing = true;
        HueFill.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));
        HueSlider.Value = _hue;
        OpacitySlider.Value = _alpha;
        OpacityValue.Text = $"{_alpha / 255.0:P0}";
        NewPreview.Background = new SolidColorBrush(_color);
        OpacityGradient.Background = new LinearGradientBrush(Color.FromArgb(0, _color.R, _color.G, _color.B),
            Color.FromRgb(_color.R, _color.G, _color.B), new Point(0, 0), new Point(1, 0));
        RgbValue.Text = $"RGB  {_color.R}, {_color.G}, {_color.B}";
        if (updateHex) HexBox.Text = SelectedHex;
        ValidationText.Visibility = Visibility.Collapsed;
        ApplyButton.IsEnabled = true;
        UpdateCursor();
        _syncing = false;
    }

    void UpdateCursor()
    {
        Canvas.SetLeft(ColorCursor, _saturation * ColorSurface.ActualWidth - 7);
        Canvas.SetTop(ColorCursor, (1 - _value) * ColorSurface.ActualHeight - 7);
    }

    void HexChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (TryParseHex(HexBox.Text, _allowAlpha, out var color)) SetColor(color, false);
        else
        {
            ApplyButton.IsEnabled = false;
            ValidationText.Text = _allowAlpha ? "Enter #RRGGBB or #AARRGGBB (alpha first)." : "Enter #RRGGBB (six hexadecimal digits).";
            ValidationText.Visibility = Visibility.Visible;
        }
    }

    void HueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _hue = e.NewValue;
        _color = FromHsv(_hue, _saturation, _value, _alpha);
        RefreshColor();
    }

    void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _alpha = (byte)Math.Round(e.NewValue);
        _color = Color.FromArgb(_alpha, _color.R, _color.G, _color.B);
        RefreshColor();
    }

    void SelectAt(Point point)
    {
        if (ColorSurface.ActualWidth <= 0 || ColorSurface.ActualHeight <= 0) return;
        _saturation = Math.Clamp(point.X / ColorSurface.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(point.Y / ColorSurface.ActualHeight, 0, 1);
        _color = FromHsv(_hue, _saturation, _value, _alpha);
        RefreshColor();
    }

    void SurfaceDown(object sender, MouseButtonEventArgs e)
    {
        ColorSurface.CaptureMouse(); SelectAt(e.GetPosition(ColorSurface)); e.Handled = true;
    }
    void SurfaceMove(object sender, MouseEventArgs e)
    {
        if (ColorSurface.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectAt(e.GetPosition(ColorSurface));
    }
    void SurfaceUp(object sender, MouseButtonEventArgs e)
    {
        if (!ColorSurface.IsMouseCaptured) return;
        SelectAt(e.GetPosition(ColorSurface)); ColorSurface.ReleaseMouseCapture(); e.Handled = true;
    }
    void SurfaceSizeChanged(object sender, SizeChangedEventArgs e) => UpdateCursor();
    void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseHex(HexBox.Text, _allowAlpha, out var color)) return;
        SetColor(color);
        DialogResult = true;
    }
}
