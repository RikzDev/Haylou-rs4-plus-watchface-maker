using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class PngFontWindow : Window
{
    readonly ObservableCollection<PngGlyphRow> _glyphs = new();
    public PngFontDefinition? Definition { get; private set; }

    public PngFontWindow(IEnumerable<string> paths)
    {
        InitializeComponent();
        GlyphGrid.ItemsSource = _glyphs;
        AddImages(paths);
    }

    public PngFontWindow(PngFontDefinition definition, string? previewText = null) : this(Array.Empty<string>())
    {
        PngFontDefinition.Validate(definition.Characters);
        // Preserve explicit character assignments, regardless of asset filenames.
        var rows = definition.Characters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PngGlyphRow(pair.Value) { Character = pair.Key }).ToList();
        Title = "Edit PNG font";
        FontNameBox.Text = definition.Label;
        UsePngFontButton.Content = "Apply changes";
        FontInstructions.Text = "Add letters, digits or punctuation, replace images, or edit character assignments. Changes apply to every text element using this PNG font.";
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, _) => UpdateMapping();
            _glyphs.Add(row);
        }
        if (previewText != null) PreviewTextBox.Text = previewText;
        UpdateMapping();
    }

    void AddImages(IEnumerable<string> paths)
    {
        // Decode the entire selection before changing the current mapping.
        var additions = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !_glyphs.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => Regex.Replace(System.IO.Path.GetFileName(path), @"\d+", match => match.Value.PadLeft(8, '0')), StringComparer.OrdinalIgnoreCase)
            .Select(path => new PngGlyphRow(path)).ToList();
        if (_glyphs.Count + additions.Count > PngFontDefinition.MaxCharacters)
            throw new InvalidDataException($"A PNG font can contain up to {PngFontDefinition.MaxCharacters} characters (printable ASCII).");
        foreach (var row in additions)
        {
            row.PropertyChanged += (_, _) => UpdateMapping();
            _glyphs.Add(row);
        }
        if (_glyphs.Count == 10 && _glyphs.All(row => row.Character.Length == 0)) AssignDigits();
        UpdateMapping();
    }

    void AddImagesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Add character images", Filter = "PNG images|*.png", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        try { AddImages(dialog.FileNames); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "PNG font", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void RemoveImagesClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in GlyphGrid.SelectedItems.Cast<PngGlyphRow>().ToList()) _glyphs.Remove(row);
        UpdateMapping();
    }

    void GlyphSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReplaceGlyphImageButton != null)
            ReplaceGlyphImageButton.IsEnabled = GlyphGrid.SelectedItems.Count == 1;
    }

    void ReplaceImageClick(object sender, RoutedEventArgs e)
    {
        if (GlyphGrid.SelectedItems.Count != 1) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Replace character image", Filter = "PNG images|*.png" };
        if (dialog.ShowDialog() != true) return;
        try { ReplaceSelectedImage(dialog.FileName); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "PNG font", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void ReplaceSelectedImage(string path)
    {
        if (GlyphGrid.SelectedItems.Count != 1 || GlyphGrid.SelectedItem is not PngGlyphRow selected) return;
        var replacement = new PngGlyphRow(path) { Character = selected.Character };
        replacement.PropertyChanged += (_, _) => UpdateMapping();
        _glyphs[_glyphs.IndexOf(selected)] = replacement;
        GlyphGrid.SelectedItem = replacement;
        UpdateMapping();
    }

    void AssignDigitsClick(object sender, RoutedEventArgs e) => AssignDigits();

    void AssignDigits()
    {
        if (_glyphs.Count != 10) return;
        for (var index = 0; index < 10; index++) _glyphs[index].Character = index.ToString();
        UpdateMapping();
    }

    PngFontDefinition CollectMapping()
    {
        if (_glyphs.Any(row => row.Character.Length != 1 || row.Character[0] is < ' ' or > '~'))
            throw new InvalidDataException("Assign a single character to each image: digits, letters or punctuation.");
        if (_glyphs.GroupBy(row => row.Character).Any(group => group.Count() > 1))
            throw new InvalidDataException("Each character must have exactly one image. Fix duplicate characters in the table.");
        var definition = new PngFontDefinition
        {
            Label = string.IsNullOrWhiteSpace(FontNameBox.Text) ? "PNG font" : FontNameBox.Text.Trim(),
            Characters = _glyphs.ToDictionary(row => row.Character, row => row.Path),
        };
        PngFontDefinition.Validate(definition.Characters);
        return definition;
    }

    void PreviewChanged(object sender, TextChangedEventArgs e) => UpdateMapping();

    void UpdateMapping()
    {
        if (UsePngFontButton == null) return;
        AssignDigitsButton.IsEnabled = _glyphs.Count == 10;
        try
        {
            var definition = CollectMapping();
            UsePngFontButton.IsEnabled = SavePngFontButton.IsEnabled = true;
            ValidationInfo.Foreground = Brushes.LightGreen;
            var allDigits = "0123456789".All(ch => definition.Characters.ContainsKey(ch.ToString()));
            ValidationInfo.Text = $"{_glyphs.Count} characters mapped." + (allDigits ? " All digits 0–9 are included." : " Include 0–9 to use numeric data sources.");
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            UsePngFontButton.IsEnabled = SavePngFontButton.IsEnabled = false;
            ValidationInfo.Foreground = Brushes.Gold;
            ValidationInfo.Text = error.Message;
        }
        FontPreview.Children.Clear();
        if (_glyphs.Count == 0) return;
        var largest = _glyphs.Max(row => Math.Max(row.Preview.PixelWidth, row.Preview.PixelHeight));
        var scale = 44.0 / largest;
        var width = _glyphs.Max(row => row.Preview.PixelWidth) * scale;
        foreach (var ch in PreviewTextBox.Text)
        {
            var row = _glyphs.FirstOrDefault(row => row.Character == ch.ToString());
            var cell = new Border { Width = width + 3, Height = 44, VerticalAlignment = VerticalAlignment.Center };
            if (row != null) cell.Child = new Image { Source = row.Preview, Width = row.Preview.PixelWidth * scale,
                Height = row.Preview.PixelHeight * scale, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            FontPreview.Children.Add(cell);
        }
    }

    void UseFontClick(object sender, RoutedEventArgs e)
    {
        Definition = CollectMapping();
        DialogResult = true;
    }

    void SaveFontClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Save reusable PNG font", Filter = "PNG font|*.hfont.json", FileName = "png-font.hfont.json" };
        if (dialog.ShowDialog() != true) return;
        try { CollectMapping().Save(dialog.FileName); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "PNG font", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}

public sealed class PngGlyphRow : INotifyPropertyChanged
{
    string _character = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Path { get; }
    public BitmapSource Preview { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Dimensions => $"{Preview.PixelWidth} × {Preview.PixelHeight}";
    public string Character
    {
        get => _character;
        set { if (_character == value) return; _character = value ?? ""; PropertyChanged?.Invoke(this, new(nameof(Character))); }
    }

    public PngGlyphRow(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        using var stream = File.OpenRead(Path);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
        Preview = image;
        var stem = System.IO.Path.GetFileNameWithoutExtension(FileName);
        Character = stem.Length == 1 && stem[0] is >= ' ' and <= '~' ? stem : MainWindow.CharacterForFile(FileName) ?? "";
    }
}
