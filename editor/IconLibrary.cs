using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    List<BuiltinIconChoice>? _iconChoices;

    List<BuiltinIconChoice> CreateBuiltinIconChoices()
    {
        if (_iconChoices != null) return _iconChoices;
        EnsureBuiltinIconLibrary();
        _iconChoices = new();
        foreach (var name in _builtinIcons!.Keys.OrderBy(IconSortKey, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var frames = RenderBuiltinDrawable(name, 44, Colors.White, new HashSet<string>());
                if (frames.Count == 0) continue;
                var label = name["ic_wf_".Length..].Replace('_', ' ');
                _iconChoices.Add(new BuiltinIconChoice
                {
                    ResourceName = name,
                    Label = char.ToUpperInvariant(label[0]) + label[1..],
                    Category = IconCategory(name),
                    Preview = frames[0],
                    FrameCount = frames.Count,
                });
            }
            catch (InvalidDataException error) { Log($"Icon library: {name}: {error.Message}", LogLevel.Warning); }
        }
        return _iconChoices;
    }

    static string IconSortKey(string name) => Regex.Replace(name, @"\d+", match => match.Value.PadLeft(8, '0'));

    static string IconCategory(string name) => name switch
    {
        _ when name.Contains("weather") => "Weather",
        _ when name.Contains("heart_rate") => "Heart rate",
        _ when name.Contains("step_") => "Steps",
        _ when name.Contains("battery") => "Battery",
        _ when name.Contains("distance") => "Distance",
        _ when name.Contains("calorie") => "Calories",
        _ when name.Contains("connection") => "Connection",
        _ when name.Contains("progress") || name.Contains("goal") => "Progress",
        _ => "Other",
    };

    void OpenIconLibraryClick(object sender, RoutedEventArgs e)
    {
        var target = _selectedAnimation?.Source == "weather" ? null : _selectedAnimation;
        var dialog = new IconLibraryWindow(CreateBuiltinIconChoices(), target?.Label, target?.Frames.Count ?? 0) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedIcons.Count == 0) return;
        try { AddBuiltinIcons(dialog.SelectedIcons.Select(icon => icon.ResourceName).ToArray(), dialog.AppendToSequence ? target : null); }
        catch (Exception error) { Log("Cannot add library frames: " + error.Message, LogLevel.Error); }
    }

    void AddBuiltinIcon(string resourceName) => AddBuiltinIcons(new[] { resourceName }, null);

    void AddBuiltinIcons(string[] resourceNames, AnimationItem? target)
    {
        if (target?.Source == "weather") throw new InvalidDataException("Use Weather icons to replace weather condition images.");
        if (resourceNames.Length == 0) return;
        EnsureBuiltinIconLibrary();
        var resolution = target == null ? 32 : Math.Clamp(Math.Max(target.Width, target.Height), 1, ScreenHeight);
        var raw = resourceNames.SelectMany(name => RenderBuiltinDrawable(name, resolution, Colors.White, new HashSet<string>())).ToList();
        if (raw.Count == 0 || raw.Count + (target?.Frames.Count ?? 0) > 64)
            throw new InvalidDataException("A sequence must contain between 1 and 64 frames. Select fewer icons.");
        var width = target?.Width ?? raw.Max(frame => frame.PixelWidth);
        var height = target?.Height ?? raw.Max(frame => frame.PixelHeight);
        var previews = raw.Select(frame => FitLibraryFrame(frame, width, height)).ToList();
        var frames = new List<string>();
        var key = Guid.NewGuid().ToString("N");
        for (var index = 0; index < previews.Count; index++)
        {
            var file = Path.Combine(_workingFolder, $"icon-{key}-{index:D2}.png");
            SavePng(previews[index], file);
            frames.Add(file);
        }
        if (target != null)
        {
            var index = ReferenceEquals(target, _selectedAnimation) && _selectedFrameIndex >= 0
                ? Math.Min(_selectedFrameIndex + 1, target.Frames.Count) : target.Frames.Count;
            var last = index + frames.Count - 1;
            for (var i = 0; i < frames.Count; i++, index++)
            {
                target.Frames.Insert(index, frames[i]);
                if (target.OriginalFrames.Count > 0) target.OriginalFrames.Insert(index, frames[i]);
                target.Previews.Insert(index, previews[i]);
                target.FrameOffsets.Insert(index, new FrameOffset());
            }
            if (target.TintColor != null) SetAnimationTint(target, target.TintColor);
            _savedBin = null;
            RefreshAnimations(target);
            RefreshFrames(last);
            Redraw();
            Log($"Added {frames.Count} library frame(s) to {target.Label}, after the selected frame. The sequence now has {target.Frames.Count} frames.");
            return;
        }
        var combined = resourceNames.Length > 1;
        var resourceName = resourceNames[0];
        var categories = resourceNames.Select(IconCategory).Distinct().ToList();
        var animation = new AnimationItem
        {
            Label = combined ? (categories.Count == 1 ? categories[0] : "Icon") + " sequence"
                : resourceName["ic_wf_".Length..].Replace('_', ' '),
            Frames = frames,
            Previews = previews,
            FrameOffsets = frames.Select(_ => new FrameOffset()).ToList(),
            Width = previews[0].PixelWidth,
            Height = previews[0].PixelHeight,
            X = 24,
            Y = 24,
            Source = frames.Count == 1 ? "static" : resourceNames.All(name => name.Contains("battery")) ? "battery"
                : !combined && (resourceName.Contains("weather") || resourceName.Contains("connection")) ? "static" : "loop",
        };
        SetAnimationTint(animation, "#FFFFFF");
        _animations.Add(animation);
        _savedBin = null;
        RefreshAnimations(animation);
        _filling = true;
        _animationPreviewMode = "live";
        _animationPreviewPlaying = true;
        AnimationPreviewModeBox.SelectedIndex = 0;
        _filling = false;
        UpdatePreviewControls();
        Redraw();
        Log($"Added built-in icon {animation.Label} ({frames.Count} frames). Change its color in the image sequence panel.");
        if (frames.Count > 1 && animation.Source == "static")
            Log("Raw weather and connection sets use a fixed frame. Use Weather icons to add automatic weather with the correct condition order. All library frames remain available in the sequence editor.", LogLevel.Warning);
    }

    static BitmapSource FitLibraryFrame(BitmapSource source, int width, int height)
    {
        var scale = Math.Min(width / (double)source.PixelWidth, height / (double)source.PixelHeight);
        var w = source.PixelWidth * scale;
        var h = source.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(source, new Rect((width - w) / 2, (height - h) / 2, w, h));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return CopyPixels(bitmap);
    }
}
