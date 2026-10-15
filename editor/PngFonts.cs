using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    static string PngFontName(string label)
    {
        const string suffix = " · PNG";
        label = label.Trim();
        if (label.EndsWith(suffix, StringComparison.Ordinal)) label = label[..^suffix.Length];
        return string.IsNullOrWhiteSpace(label) ? "PNG font" : label;
    }

    void EditPngFontClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || FontChoiceBox.SelectedItem is not FontAsset font || font.Characters.Count == 0) return;
        try
        {
            var dialog = new PngFontWindow(new PngFontDefinition
            {
                Label = PngFontName(font.Label), Characters = new(font.Characters),
            }, _selected.Sample) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Definition != null)
                UpdatePngFont(font, dialog.Definition);
        }
        catch (Exception error) { Log("Cannot edit PNG font: " + error.Message, LogLevel.Error); }
    }

    void UpdatePngFont(FontAsset font, PngFontDefinition definition)
    {
        var index = _availableFonts.IndexOf(font);
        if (index < 0 || font.Characters.Count == 0) throw new InvalidOperationException("Select a loaded PNG font to edit.");
        PngFontDefinition.Validate(definition.Characters);
        var affected = _widgets.Where(widget => SameCharacters(widget.CharacterImages, font.Characters)).ToList();
        // Decode and snapshot everything before changing the font or any element.
        // Existing project/font files remain untouched until explicitly saved.
        var previews = definition.Characters.ToDictionary(pair => pair.Key, pair => LoadBitmap(pair.Value));
        var assets = CopyPngFontAssets(previews);
        var edited = new FontAsset { Label = PngFontName(definition.Label) + " · PNG", Characters = assets };
        _availableFonts[index] = edited;
        foreach (var widget in affected)
        {
            widget.FontFile = null;
            widget.CharacterImages = new(assets);
            widget.CharacterPreviews = new(previews);
            // Editing the font preserves each element's size, spacing and placement.
            WarnMissingPngCharacters(widget, edited);
        }
        _savedBin = null;
        RefreshFontChoices();
        ShowSelection();
        Redraw();
        Log($"PNG font updated: {PngFontName(definition.Label)}, {assets.Count} characters, {affected.Count} text elements.");
    }

    Dictionary<string, string> CopyPngFontAssets(Dictionary<string, BitmapSource> previews)
    {
        var folder = Path.Combine(_workingFolder, "png-font-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var assets = new Dictionary<string, string>();
        foreach (var pair in previews)
        {
            var file = Path.Combine(folder, $"c{(int)pair.Key[0]:D3}.png");
            SavePng(pair.Value, file);
            assets[pair.Key] = file;
        }
        return assets;
    }

    void WarnMissingPngCharacters(Widget widget, FontAsset font)
    {
        var needed = widget.Source switch { "text" => widget.Sample, "ampm" => "APM", "distance_unit" => "kmi", _ => "0123456789" };
        var missing = new string(needed.Where(ch => ch is not ' ' and not '-' and not '.' && !font.Characters.ContainsKey(ch.ToString())).Distinct().ToArray());
        if (missing.Length > 0)
            Log($"{widget.Label} needs characters absent from this PNG font: {missing}. Add those images or change the text/data source.", LogLevel.Warning);
    }

    void LoadPngFontClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a PNG font or select character images",
            Filter = "PNG fonts and images|*.hfont.json;*.png|PNG font|*.hfont.json|PNG images|*.png",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (dialog.FileNames.Length == 1 && dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                LoadPngFont(dialog.FileName);
            else
            {
                if (dialog.FileNames.Any(file => !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Select one saved PNG font, or a set of PNG character images.");
                var mapping = new PngFontWindow(dialog.FileNames) { Owner = this };
                if (mapping.ShowDialog() == true && mapping.Definition != null)
                    UsePngFont(mapping.Definition);
            }
        }
        catch (Exception error) { Log("Cannot load PNG font: " + error.Message, LogLevel.Error); }
    }

    void LoadPngFont(string path)
    {
        var definition = JsonSerializer.Deserialize<PngFontDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty PNG font.");
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        definition.Characters = definition.Characters.ToDictionary(pair => pair.Key, pair => Path.GetFullPath(Path.Combine(folder, pair.Value)));
        UsePngFont(definition);
    }

    FontAsset RegisterPngFont(Dictionary<string, string> characters, string label, bool copyAssets)
    {
        PngFontDefinition.Validate(characters);
        var existing = _availableFonts.FirstOrDefault(font => SameCharacters(font.Characters, characters));
        if (existing != null) return existing;
        // Validate and decode every image before changing the font catalogue.
        var previews = characters.ToDictionary(pair => pair.Key, pair => LoadBitmap(pair.Value));
        var assets = copyAssets ? CopyPngFontAssets(previews) : new Dictionary<string, string>(characters);
        var font = new FontAsset { Label = label, Characters = assets };
        _availableFonts.Add(font);
        return font;
    }

    void UsePngFont(PngFontDefinition definition)
    {
        var label = PngFontName(definition.Label) + " · PNG";
        var font = RegisterPngFont(definition.Characters, label, true);
        RefreshFontChoices();
        if (_selected != null)
        {
            ApplyFont(_selected, font);
            WarnMissingPngCharacters(_selected, font);
        }
        Log($"PNG font loaded: {definition.Label}, {font.Characters.Count} characters. Choose it for other text elements in the font list.");
    }
}

public sealed class PngFontDefinition
{
    public const int MaxCharacters = 95;
    public string Label { get; set; } = "PNG font";
    public Dictionary<string, string> Characters { get; set; } = new();

    public static void Validate(Dictionary<string, string> characters)
    {
        if (characters.Count is < 1 or > MaxCharacters)
            throw new InvalidDataException($"Choose between 1 and {MaxCharacters} character images (printable ASCII).");
        foreach (var pair in characters)
        {
            if (pair.Key.Length != 1 || pair.Key[0] is < ' ' or > '~')
                throw new InvalidDataException("Assign one supported character to each image: digits, letters or punctuation.");
            if (string.IsNullOrWhiteSpace(pair.Value) || !File.Exists(pair.Value))
                throw new FileNotFoundException($"Missing image for character '{pair.Key}': {pair.Value}");
        }
    }

    public void Save(string path)
    {
        Validate(Characters);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var assetFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-images");
        // Snapshot first so saving a remapped font cannot overwrite a later source.
        var data = Characters.ToDictionary(pair => pair.Key, pair => File.ReadAllBytes(pair.Value));
        var relative = new Dictionary<string, string>();
        Directory.CreateDirectory(assetFolder);
        foreach (var pair in data)
        {
            var file = Path.Combine(assetFolder, $"c{(int)pair.Key[0]:D3}.png");
            File.WriteAllBytes(file, pair.Value);
            relative[pair.Key] = Path.GetRelativePath(folder, file);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new PngFontDefinition { Label = Label, Characters = relative },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
