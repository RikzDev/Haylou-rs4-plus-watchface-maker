using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    readonly List<FontAsset> _availableFonts = new();
    readonly Dictionary<string, string> _zipFonts = new(StringComparer.OrdinalIgnoreCase);
    static string DefaultFontPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");

    sealed class FontAsset
    {
        public string Label { get; init; } = "";
        public string? File { get; init; }
        public Dictionary<string, string> Characters { get; init; } = new();
        public override string ToString() => Label;
    }

    void ResetFonts()
    {
        _availableFonts.Clear(); _zipFonts.Clear(); _fontFaces.Clear();
        RegisterFont(DefaultFontPath, "Consolas");
    }

    FontAsset RegisterFont(string file, string? label = null)
    {
        file = Path.GetFullPath(file);
        var existing = _availableFonts.FirstOrDefault(font => string.Equals(font.File, file, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var asset = new FontAsset { File = file, Label = label ?? Path.GetFileName(file) };
        _availableFonts.Add(asset);
        return asset;
    }

    void ImportFonts(ZipArchive zip)
    {
        ResetFonts();
        var folder = Path.Combine(_workingFolder, "fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var entry in zip.Entries.Where(entry => Path.GetExtension(entry.FullName).ToLowerInvariant() is ".ttf" or ".otf"))
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            var file = Path.Combine(folder, $"{_zipFonts.Count:D2}_" + Path.GetFileName(name));
            using (var input = entry.Open())
            using (var output = System.IO.File.Create(file)) input.CopyTo(output);
            _zipFonts[name] = file;
            RegisterFont(file, Path.GetFileName(name) + " · TBWF");
        }
    }

    void RefreshFontChoices()
    {
        foreach (var widget in _widgets)
        {
            if (!string.IsNullOrEmpty(widget.FontFile)) RegisterFont(widget.FontFile);
            if (widget.CharacterImages.Count > 0 && !_availableFonts.Any(font => SameCharacters(font.Characters, widget.CharacterImages)))
                _availableFonts.Add(new FontAsset { Label = widget.Label + " · PNG characters", Characters = new(widget.CharacterImages) });
        }
        var filling = _filling; _filling = true;
        FontChoiceBox.ItemsSource = null;
        FontChoiceBox.ItemsSource = _availableFonts;
        ShowFontChoice();
        _filling = filling;
    }

    static bool SameCharacters(Dictionary<string, string> left, Dictionary<string, string> right) =>
        left.Count > 0 && left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var file) && file == pair.Value);

    void ShowFontChoice()
    {
        FontChoiceBox.IsEnabled = _selected != null;
        LoadFontButton.IsEnabled = _selected != null;
        LoadPngFontButton.IsEnabled = _selected != null;
        FontChoiceBox.SelectedItem = _selected == null ? null : _availableFonts.FirstOrDefault(font =>
            _selected.CharacterImages.Count > 0 ? SameCharacters(font.Characters, _selected.CharacterImages)
                : string.Equals(font.File, _selected.FontFile ?? DefaultFontPath, StringComparison.OrdinalIgnoreCase));
        EditPngFontButton.IsEnabled = !_layerBusy && _selected?.CharacterImages.Count > 0;
    }

    void ApplyFont(Widget widget, FontAsset font)
    {
        widget.FontFile = font.File;
        widget.CharacterImages = new(font.Characters);
        widget.CharacterPreviews = font.Characters.ToDictionary(pair => pair.Key, pair => LoadBitmap(pair.Value));
        widget.CharacterSpacing = 0;
        _savedBin = null;
        ShowSelection(); RefreshWidgetVisual();
    }

    void FontChoiceChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_filling || _selected == null || FontChoiceBox.SelectedItem is not FontAsset font) return;
        ApplyFont(_selected, font);
    }

    void LoadFontClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Load a font for the text", Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf" };
        if (dialog.ShowDialog() != true) return;
        try { LoadCustomFont(dialog.FileName); }
        catch (Exception ex) { Log("Could not load the font: " + ex.Message, LogLevel.Error); }
    }

    void LoadCustomFont(string path)
    {
        _ = new GlyphTypeface(new Uri(Path.GetFullPath(path)));
        var folder = Path.Combine(_workingFolder, "fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Path.GetFileName(path));
        System.IO.File.Copy(path, file);
        var font = RegisterFont(file);
        RefreshFontChoices();
        if (_selected != null) ApplyFont(_selected, font);
        Log("Font loaded: " + Path.GetFileName(path));
    }
}
