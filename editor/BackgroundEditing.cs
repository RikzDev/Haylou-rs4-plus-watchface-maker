using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    BitmapSource? _backgroundOriginal;
    string _backgroundName = "";
    string _backgroundColor = "#000000";
    string _backgroundFit = "cover";
    double _backgroundZoom = 100;
    int _backgroundX, _backgroundY;
    bool _backgroundReady, _syncingBackground;

    void ChooseBackgroundClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose background image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SetBackgroundImage(LoadBitmap(dialog.FileName), Path.GetFileName(dialog.FileName));
            Log($"Background changed: {dialog.FileName}");
        }
        catch (Exception ex) { Log("Background error: " + ex.Message, LogLevel.Error); }
    }

    void SetBackgroundImage(BitmapSource? image, string name = "")
    {
        // Keep the original pixels in memory so edits never resample an earlier crop.
        _backgroundOriginal = image == null ? null : CopyPixels(image);
        _backgroundName = name;
        _backgroundFit = "cover";
        _backgroundZoom = 100;
        _backgroundX = _backgroundY = 0;
        RenderBackground();
    }

    void RemoveBackgroundClick(object sender, RoutedEventArgs e) => SetBackgroundImage(null);

    void ResetBackgroundPlacementClick(object sender, RoutedEventArgs e)
    {
        _backgroundFit = "cover";
        _backgroundZoom = 100;
        _backgroundX = _backgroundY = 0;
        RenderBackground();
    }

    void BackgroundFitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_backgroundReady || _syncingBackground || BackgroundFitBox.SelectedItem is not ComboBoxItem { Tag: string mode }) return;
        _backgroundFit = mode;
        RenderBackground();
    }

    void BackgroundColorChanged(object sender, TextChangedEventArgs e)
    {
        if (!_backgroundReady || _syncingBackground) return;
        var value = BackgroundColorBox.Text.Trim();
        if (value.Length != 7 || !ValidImageColor(value)) return;
        _backgroundColor = value.ToUpperInvariant();
        RenderBackground();
    }

    void BackgroundColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) BackgroundColorBox.Text = color;
    }

    void BackgroundZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_backgroundReady || _syncingBackground) return;
        _backgroundZoom = BackgroundZoomSlider.Value;
        RenderBackground();
    }

    void BackgroundPositionChanged(object sender, TextChangedEventArgs e)
    {
        if (!_backgroundReady || _syncingBackground) return;
        if (!int.TryParse(BackgroundXBox.Text, out var x) || !int.TryParse(BackgroundYBox.Text, out var y)
            || Math.Abs((long)x) > ScreenWidth * 10 || Math.Abs((long)y) > ScreenHeight * 10) return;
        _backgroundX = x;
        _backgroundY = y;
        RenderBackground();
    }

    void RefreshBackgroundControls()
    {
        _syncingBackground = true;
        try
        {
            var hasImage = _backgroundOriginal != null;
            BackgroundFileInfo.Text = hasImage
                ? $"{_backgroundName} · {_backgroundOriginal!.PixelWidth} × {_backgroundOriginal.PixelHeight}"
                : "Solid color · 368 × 448";
            BackgroundColorBox.Text = _backgroundColor;
            BackgroundFitBox.SelectedItem = BackgroundFitBox.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == _backgroundFit);
            BackgroundZoomSlider.Value = _backgroundZoom;
            BackgroundZoomInfo.Text = $"Zoom · {_backgroundZoom:0}%";
            BackgroundXBox.Text = _backgroundX.ToString();
            BackgroundYBox.Text = _backgroundY.ToString();
            BackgroundFitBox.IsEnabled = BackgroundZoomSlider.IsEnabled = hasImage;
            BackgroundXBox.IsEnabled = BackgroundYBox.IsEnabled = hasImage;
            RemoveBackgroundButton.IsEnabled = ResetBackgroundPlacementButton.IsEnabled = hasImage;
        }
        finally { _syncingBackground = false; }
    }

    void RenderBackground()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush(_backgroundColor), null, new Rect(0, 0, ScreenWidth, ScreenHeight));
            if (_backgroundOriginal is { } image)
            {
                double sx = ScreenWidth / (double)image.PixelWidth;
                double sy = ScreenHeight / (double)image.PixelHeight;
                var scale = _backgroundFit switch { "fit" => Math.Min(sx, sy), "center" => 1, _ => Math.Max(sx, sy) };
                double width = (_backgroundFit == "stretch" ? ScreenWidth : image.PixelWidth * scale) * _backgroundZoom / 100;
                double height = (_backgroundFit == "stretch" ? ScreenHeight : image.PixelHeight * scale) * _backgroundZoom / 100;
                dc.DrawImage(image, new Rect((ScreenWidth - width) / 2 + _backgroundX,
                    (ScreenHeight - height) / 2 + _backgroundY, width, height));
            }
        }
        var bitmap = new RenderTargetBitmap(ScreenWidth, ScreenHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        // Edits stay in the working folder until Save project is used.
        _fittedPng = Path.Combine(_workingFolder, "background-preview.png");
        SavePng(bitmap, _fittedPng);
        BackgroundImage.Source = bitmap;
        _savedBin = null;
        _backgroundReady = true;
        RefreshBackgroundControls();
    }

    SavedBackground SaveBackgroundSettings(string projectPath)
    {
        var image = "";
        if (_backgroundOriginal != null)
        {
            image = Path.GetFileNameWithoutExtension(projectPath) + "-background.png";
            SavePng(_backgroundOriginal, Path.Combine(Path.GetDirectoryName(projectPath)!, image));
        }
        return new SavedBackground
        {
            Image = image, Name = _backgroundName, Color = _backgroundColor, Fit = _backgroundFit,
            Zoom = _backgroundZoom, X = _backgroundX, Y = _backgroundY,
        };
    }

    void LoadBackgroundSettings(ProjectFile project, string projectPath)
    {
        var folder = Path.GetDirectoryName(projectPath)!;
        var settings = project.BackgroundEdit;
        var rendered = Path.Combine(folder, project.Background);
        _backgroundColor = "#000000";
        if (settings != null && (string.IsNullOrEmpty(settings.Image) || File.Exists(Path.Combine(folder, settings.Image))))
        {
            _backgroundOriginal = string.IsNullOrEmpty(settings.Image) ? null : LoadBitmap(Path.Combine(folder, settings.Image));
            _backgroundName = settings.Name;
            _backgroundColor = settings.Color.Length == 7 && ValidImageColor(settings.Color) ? settings.Color : "#000000";
            _backgroundFit = settings.Fit is "cover" or "fit" or "stretch" or "center" ? settings.Fit : "cover";
            _backgroundZoom = double.IsFinite(settings.Zoom) ? Math.Clamp(settings.Zoom, 10, 400) : 100;
            _backgroundX = Math.Clamp(settings.X, -ScreenWidth * 10, ScreenWidth * 10);
            _backgroundY = Math.Clamp(settings.Y, -ScreenHeight * 10, ScreenHeight * 10);
            RenderBackground();
            return;
        }
        // Older projects contain only the rendered PNG, which remains editable.
        SetBackgroundImage(File.Exists(rendered) ? LoadBitmap(rendered) : null, "Project background");
        if (settings != null)
            Log("Original background image not found. Using the saved preview when available.", LogLevel.Warning);
        else if (!File.Exists(rendered) && !string.IsNullOrWhiteSpace(project.Background))
            Log($"Background not found: {rendered}. Using a black background.", LogLevel.Warning);
    }
}

public sealed class SavedBackground
{
    public string Image { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#000000";
    public string Fit { get; set; } = "cover";
    public double Zoom { get; set; } = 100;
    public int X { get; set; }
    public int Y { get; set; }
}
