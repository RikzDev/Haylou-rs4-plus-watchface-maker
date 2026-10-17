using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    // Slot 0 is the fallback; slots 1..12 match the official LS11 codes.
    internal static readonly WeatherIconSlot[] WeatherSlots =
    {
        new("no_data", "No data / unknown", "unknown"),
        new("clear_day", "Clear sky / day", "clear_sky_day"),
        new("partly_cloudy_day", "Partly cloudy / day", "scattered_clouds_day"),
        new("overcast", "Overcast", "cloudy_day"),
        new("showers_day", "Showers / day", "rain_with_sun_day"),
        new("thunderstorm", "Thunderstorm", "thunderstorm"),
        new("sleet", "Rain and snow", "rain_and_snow"),
        new("light_rain", "Light rain", "light_rain"),
        new("heavy_rain", "Heavy rain", "medium_rain"),
        new("snow", "Snow", "medium_snow", "heavy_snow"),
        new("sandstorm", "Sandstorm", "sandstorm"),
        new("haze", "Haze", "haze"),
        new("wind", "Wind", "wind"),
        new("clear_night", "Clear sky / night", "clear_sky_night"),
        new("partly_cloudy_night", "Partly cloudy / night", "scattered_clouds_night"),
        new("showers_night", "Showers / night", "rain_with_sun_night"),
    };

    string _weatherIconStyle = "fill";
    Dictionary<string, string> _weatherIconOverrides = new(StringComparer.Ordinal);
    static string WeatherUserSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaylouFaceEditor", "weather-icons", "defaults.json");

    void InitializeWeatherIcons()
    {
        _weatherIconStyle = "fill";
        _weatherIconOverrides = new(StringComparer.Ordinal);
        if (!File.Exists(WeatherUserSettings)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedWeatherIconSet>(File.ReadAllText(WeatherUserSettings))
                ?? throw new InvalidDataException("Empty weather icon settings.");
            LoadWeatherIcons(saved, WeatherUserSettings);
        }
        catch (Exception error) { Log("Cannot load your default weather icons: " + error.Message, LogLevel.Warning); }
    }

    BitmapSource DefaultWeatherIcon(WeatherIconSlot slot, string style)
    {
        EnsureBuiltinIconLibrary();
        return RenderBuiltinDrawable(slot.ResourceName(style), 64, Colors.White, new HashSet<string>(), true)[0];
    }

    List<WeatherIconRow> CreateWeatherIconRows() => WeatherSlots.Select(slot =>
    {
        var file = _weatherIconOverrides.GetValueOrDefault(slot.Key);
        return new WeatherIconRow(slot.Key, slot.Label, file == null ? DefaultWeatherIcon(slot, _weatherIconStyle) : LoadBitmap(file), file);
    }).ToList();

    void OpenWeatherIconsClick(object sender, RoutedEventArgs e)
    {
        if (_layerBusy) return;
        try
        {
            var dialog = new WeatherIconsWindow(_weatherIconStyle, CreateWeatherIconRows(), DefaultWeatherIcon) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            ApplyWeatherIcons(dialog.SelectedStyle, dialog.Rows.ToList());
            if (dialog.RememberAsDefaults)
            {
                try { SaveUserWeatherIcons(); }
                catch (Exception error) { Log("Icons applied to this project, but your defaults could not be saved: " + error.Message, LogLevel.Warning); }
            }
            if (dialog.AddToFace) AddWeatherElement();
        }
        catch (Exception error) { Log("Weather icons: " + error.Message, LogLevel.Error); }
    }

    static void ValidateWeatherStyle(string style)
    {
        if (style is not ("fill" or "outline")) throw new InvalidDataException("Choose Filled or Outline weather icons.");
    }

    void ApplyWeatherIcons(string style, IReadOnlyList<WeatherIconRow> rows)
    {
        if (_layerBusy) return;
        ValidateWeatherStyle(style);
        if (rows.Count != WeatherSlots.Length || rows.Select(row => row.Key).Distinct().Count() != WeatherSlots.Length
            || rows.Any(row => !WeatherSlots.Any(slot => slot.Key == row.Key)))
            throw new InvalidDataException("The weather set must contain all 16 unique condition slots.");
        // Decode everything first. Invalid input and Cancel never change the project.
        var decoded = rows.Where(row => row.CustomFile != null).ToDictionary(row => row.Key, row => LoadBitmap(row.CustomFile!));
        var folder = Path.Combine(_workingFolder, "weather-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in decoded)
        {
            var path = Path.Combine(folder, pair.Key + ".png");
            SavePng(pair.Value, path);
            overrides.Add(pair.Key, path);
        }
        var assets = CreateWeatherAssets(style, overrides);
        var prepared = new List<(AnimationItem Animation, List<string> Frames, List<BitmapSource> Previews)>();
        foreach (var animation in _animations.Where(item => item.Source == "weather"))
        {
            var frames = assets.Frames.ToList();
            var previews = assets.Previews.ToList();
            if (animation.TintColor != null)
            {
                var color = ColorOf(animation.TintColor);
                for (int index = 0; index < frames.Count; index++)
                {
                    previews[index] = TintBitmap(previews[index], color);
                    frames[index] = Path.Combine(folder, $"tint-{animation.AssetKey}-{index:D2}.png");
                    SavePng(previews[index], frames[index]);
                }
            }
            prepared.Add((animation, frames, previews));
        }
        _weatherIconStyle = style;
        _weatherIconOverrides = overrides;
        foreach (var entry in prepared)
        {
            entry.Animation.Frames = entry.Frames;
            entry.Animation.Previews = entry.Previews;
            entry.Animation.OriginalFrames = entry.Animation.TintColor == null ? new() : assets.Frames.ToList();
        }
        var target = _canvasTarget;
        _savedBin = null;
        RefreshAnimations(_selectedAnimation);
        _canvasTarget = target;
        Redraw();
        Log($"Weather icons updated: {overrides.Count} custom images. Existing weather elements keep their position, size, layers and frame alignment.");
    }

    (List<string> Frames, List<BitmapSource> Previews) CreateWeatherAssets(string style, Dictionary<string, string> overrides)
    {
        var key = Guid.NewGuid().ToString("N");
        var frames = new List<string>();
        var previews = new List<BitmapSource>();
        for (int index = 0; index < WeatherSlots.Length; index++)
        {
            var slot = WeatherSlots[index];
            var image = overrides.TryGetValue(slot.Key, out var file) ? LoadBitmap(file) : DefaultWeatherIcon(slot, style);
            var preview = FitLibraryFrame(image, 64, 64);
            var path = Path.Combine(_workingFolder, $"weather-{key}-{index:D2}-{slot.Key}.png");
            SavePng(preview, path);
            frames.Add(path); previews.Add(preview);
        }
        return (frames, previews);
    }

    void AddWeatherElement()
    {
        if (_layerBusy) return;
        var assets = CreateWeatherAssets(_weatherIconStyle, _weatherIconOverrides);
        var animation = new AnimationItem
        {
            Label = "Weather", Source = "weather", Frames = assets.Frames, Previews = assets.Previews,
            FrameOffsets = WeatherSlots.Select(_ => new FrameOffset()).ToList(), X = 24, Y = 24, Width = 48, Height = 48,
        };
        _animations.Add(animation);
        _savedBin = null;
        RefreshAnimations(animation);
        _animationPreviewMode = "live";
        _animationPreviewPlaying = true;
        _filling = true; AnimationPreviewModeBox.SelectedIndex = 0; _filling = false;
        UpdatePreviewControls(); Redraw();
        Log("Weather element added. Preview shows clear daytime weather; the watch selects the synchronized condition automatically.");
    }

    SavedWeatherIconSet SaveWeatherIcons(string projectPath)
    {
        var folder = Path.GetDirectoryName(projectPath)!;
        var assetFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(projectPath) + "-weather");
        var saved = new SavedWeatherIconSet { Style = _weatherIconStyle };
        var bytes = _weatherIconOverrides.ToDictionary(pair => pair.Key, pair => File.ReadAllBytes(pair.Value));
        if (bytes.Count > 0) Directory.CreateDirectory(assetFolder);
        foreach (var pair in bytes)
        {
            var output = Path.Combine(assetFolder, pair.Key + ".png");
            File.WriteAllBytes(output, pair.Value);
            saved.Overrides.Add(pair.Key, Path.GetRelativePath(folder, output));
        }
        return saved;
    }

    void LoadWeatherIcons(SavedWeatherIconSet? settings, string projectPath)
    {
        if (settings == null) { InitializeWeatherIcons(); return; }
        ValidateWeatherStyle(settings.Style);
        if (settings.Overrides.Keys.Any(key => !WeatherSlots.Any(slot => slot.Key == key)))
            throw new InvalidDataException("Unknown weather condition in the saved icon set.");
        var images = settings.Overrides.ToDictionary(pair => pair.Key,
            pair => LoadBitmap(Path.Combine(Path.GetDirectoryName(projectPath)!, pair.Value)));
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);
        var id = Guid.NewGuid().ToString("N");
        foreach (var pair in images)
        {
            var path = Path.Combine(_workingFolder, $"weather-import-{id}-{pair.Key}.png");
            SavePng(pair.Value, path); copies.Add(pair.Key, path);
        }
        _weatherIconStyle = settings.Style;
        _weatherIconOverrides = copies;
    }

    void SaveUserWeatherIcons()
    {
        var root = Path.GetDirectoryName(WeatherUserSettings)!;
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "assets-" + Guid.NewGuid().ToString("N"));
        var settings = new SavedWeatherIconSet { Style = _weatherIconStyle };
        if (_weatherIconOverrides.Count > 0) Directory.CreateDirectory(folder);
        foreach (var pair in _weatherIconOverrides)
        {
            var path = Path.Combine(folder, pair.Key + ".png");
            File.WriteAllBytes(path, File.ReadAllBytes(pair.Value));
            settings.Overrides.Add(pair.Key, Path.GetRelativePath(root, path));
        }
        var temporary = WeatherUserSettings + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, WeatherUserSettings, true);
        Log("Weather icons saved as your defaults for new projects. Existing saved projects keep their own icon settings.");
    }
}

public sealed record WeatherIconSlot(string Key, string Label, string Suffix, string? OutlineSuffix = null)
{
    public string ResourceName(string style) => $"ic_wf_weather_{style}_{(style == "outline" ? OutlineSuffix ?? Suffix : Suffix)}";
}
public sealed record WeatherIconRow(string Key, string Label, BitmapSource Preview, string? CustomFile)
{
    public string Origin => CustomFile == null ? "Built-in default" : "Custom image";
}
public sealed class SavedWeatherIconSet
{
    public string Style { get; set; } = "fill";
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);
}
