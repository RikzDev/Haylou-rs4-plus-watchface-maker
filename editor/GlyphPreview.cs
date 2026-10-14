using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    readonly Dictionary<Widget, (BitmapSource Image, double Size, string Identity)> _glyphPreviews = new();
    DispatcherTimer? _glyphPreviewTimer;
    Task? _glyphPreviewTask;
    string? _glyphSignature, _failedGlyphSignature;

    void InitializeGlyphPreview()
    {
        _glyphPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _glyphPreviewTimer.Tick += async (_, _) =>
        {
            _glyphPreviewTimer.Stop();
            await RefreshGlyphPreviewAsync();
        };
        Closed += (_, _) => { _glyphPreviewTimer.Stop(); _previewTimer.Stop(); };
    }

    object[] GlyphPreviewItems() => _widgets.Select(widget => (object)new
    {
        x = 0, y = 0, size = (int)Math.Round(widget.Size), color = widget.Color,
        source = widget.Source, text = widget.Sample, font = widget.FontFile ?? DefaultFontPath,
        characters = widget.CharacterImages, spacing = widget.CharacterSpacing,
    }).ToArray();

    static string GlyphIdentity(Widget widget) => JsonSerializer.Serialize(new
    {
        widget.Color, widget.Source, widget.Sample, widget.FontFile, widget.CharacterImages, widget.CharacterSpacing,
    });

    void QueueGlyphPreview()
    {
        if (_glyphPreviewTimer == null) return;
        var signature = JsonSerializer.Serialize(GlyphPreviewItems());
        if (signature == _glyphSignature || signature == _failedGlyphSignature) return;
        _glyphPreviewTimer.Stop(); _glyphPreviewTimer.Start();
    }

    async Task RefreshGlyphPreviewAsync()
    {
        if (_glyphPreviewTask != null) await _glyphPreviewTask;
        var items = GlyphPreviewItems();
        var signature = JsonSerializer.Serialize(items);
        if (signature == _glyphSignature || signature == _failedGlyphSignature) return;
        _glyphPreviewTask = GenerateGlyphPreviewAsync(items, signature, _widgets.ToArray());
        try { await _glyphPreviewTask; }
        finally { _glyphPreviewTask = null; QueueGlyphPreview(); }
    }

    async Task GenerateGlyphPreviewAsync(object[] items, string signature, Widget[] targets)
    {
        var folder = Path.Combine(_workingFolder, "preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var layout = Path.Combine(folder, "layout.json");
        File.WriteAllText(layout, JsonSerializer.Serialize(new { widgets = items }));
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = Python(), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true, StandardErrorEncoding = Encoding.UTF8,
            }};
            process.StartInfo.ArgumentList.Add(Path.Combine(WatchfaceRoot, "preview_layout.py"));
            process.StartInfo.ArgumentList.Add("--layout"); process.StartInfo.ArgumentList.Add(layout);
            process.StartInfo.ArgumentList.Add("--output"); process.StartInfo.ArgumentList.Add(folder);
            process.Start();
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await errors; await output;
            if (process.ExitCode != 0) throw new InvalidDataException(error.Trim());
            if (!_widgets.SequenceEqual(targets) || signature != JsonSerializer.Serialize(GlyphPreviewItems())) return;
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            _glyphPreviews.Clear();
            foreach (var record in document.RootElement.EnumerateArray())
            {
                var widget = targets[record.GetProperty("index").GetInt32()];
                _glyphPreviews[widget] = (LoadBitmap(Path.Combine(folder, record.GetProperty("file").GetString()!)),
                    Math.Round(widget.Size), GlyphIdentity(widget));
            }
            _glyphSignature = signature;
            _failedGlyphSignature = null;
            foreach (var border in Overlay.Children.OfType<Border>())
                if (border.Tag is Widget widget) border.Child = WidgetVisual(widget);
            UpdateCanvasSelection();
        }
        catch (Exception ex)
        {
            if (signature != JsonSerializer.Serialize(GlyphPreviewItems())) return;
            _failedGlyphSignature = signature;
            Log("Text preview failed: " + ex.Message, LogLevel.Error);
        }
    }
}
