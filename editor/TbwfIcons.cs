using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    Dictionary<string, string>? _builtinIcons;

    void EnsureBuiltinIconLibrary()
    {
        if (_builtinIcons != null) return;
        var assembly = typeof(MainWindow).Assembly;
        const string prefix = "HaylouFaceEditor.Assets.TbwfIcons.";
        _builtinIcons = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal))
            .ToDictionary(name => name[prefix.Length..^4], name => "embedded:" + name, StringComparer.OrdinalIgnoreCase);
    }

    List<BitmapSource> BuiltinIconFrames(string resourcePath, JsonElement element, int target)
    {
        var name = Path.GetFileNameWithoutExtension(resourcePath.Replace('\\', '/'));
        var prefix = name.IndexOf("ic_wf_", StringComparison.Ordinal);
        if (prefix < 0) return new();
        name = name[prefix..];
        EnsureBuiltinIconLibrary();
        if (_builtinIcons == null || !_builtinIcons.ContainsKey(name)) return new();
        // Keep an untinted alpha mask so subsequent color changes are reversible.
        return RenderBuiltinDrawable(name, target, Colors.White, new HashSet<string>());
    }

    static Color ReadIconTint(JsonElement element)
    {
        if (element.TryGetProperty("color", out var value))
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("colors", out var colors)
                && colors.ValueKind == JsonValueKind.Array && colors.GetArrayLength() > 0) value = colors[0];
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var argb))
                return Color.FromArgb((byte)((uint)argb >> 24), (byte)((uint)argb >> 16), (byte)((uint)argb >> 8), (byte)argb);
        }
        return Colors.White;
    }

    List<BitmapSource> RenderBuiltinDrawable(string name, int target, Color tint, HashSet<string> ancestors)
    {
        if (!ancestors.Add(name)) throw new InvalidDataException("Circular Android drawable reference: " + name);
        try
        {
            if (_builtinIcons == null || !_builtinIcons.TryGetValue(name, out var path))
                throw new FileNotFoundException("Missing built-in drawable: " + name);
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return new() { LoadBitmap(path) };
            XElement root;
            if (path.StartsWith("embedded:", StringComparison.Ordinal))
            {
                using var resource = typeof(MainWindow).Assembly.GetManifestResourceStream(path[9..])
                    ?? throw new FileNotFoundException("Missing embedded icon: " + name);
                root = XDocument.Load(resource).Root ?? throw new InvalidDataException("Empty Android drawable.");
            }
            else root = XDocument.Load(path).Root ?? throw new InvalidDataException("Empty Android drawable.");
            if (root.Name.LocalName == "animation-list")
            {
                var frames = new List<BitmapSource>();
                foreach (var item in root.Elements("item"))
                {
                    var reference = (string?)item.Attribute("drawable") ?? "";
                    frames.AddRange(RenderBuiltinDrawable(reference.Split('/').Last(), target, tint, ancestors));
                }
                return frames;
            }
            if (root.Name.LocalName != "vector")
                throw new InvalidDataException("Unsupported Android drawable type: " + root.Name.LocalName);
            var vw = IconNumber(root, "viewportWidth", 24);
            var vh = IconNumber(root, "viewportHeight", 24);
            var scale = target / Math.Max(vw, vh);
            var width = Math.Max(1, (int)Math.Round(vw * scale));
            var height = Math.Max(1, (int)Math.Round(vh * scale));
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.PushTransform(new ScaleTransform(width / vw, height / vh));
                drawing.PushOpacity(IconNumber(root, "alpha", 1));
                DrawIconNodes(drawing, root, tint);
                drawing.Pop();
                drawing.Pop();
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return new() { CopyPixels(bitmap) };
        }
        finally { ancestors.Remove(name); }
    }

    static double IconNumber(XElement element, string attribute, double fallback) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    static Brush? IconBrush(XElement node, string attribute, string alphaAttribute, Color tint)
    {
        var value = (string?)node.Attribute(attribute);
        if (value == null) return null;
        // Android theme colors and the TBWF color filter both use the face tint.
        var alpha = value.StartsWith('#') ? ((Color)ColorConverter.ConvertFromString(value)).A : (byte)255;
        return new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * tint.A / 255.0 * IconNumber(node, alphaAttribute, 1)), 0, 255), tint.R, tint.G, tint.B));
    }

    static void DrawIconNodes(DrawingContext drawing, XElement parent, Color tint)
    {
        var clips = 0;
        foreach (var node in parent.Elements())
        {
            if (node.Name.LocalName == "group")
            {
                var px = IconNumber(node, "pivotX", 0);
                var py = IconNumber(node, "pivotY", 0);
                var matrix = Matrix.Identity;
                matrix.Translate(-px, -py);
                matrix.Scale(IconNumber(node, "scaleX", 1), IconNumber(node, "scaleY", 1));
                matrix.Rotate(IconNumber(node, "rotation", 0));
                matrix.Translate(px + IconNumber(node, "translateX", 0), py + IconNumber(node, "translateY", 0));
                drawing.PushTransform(new MatrixTransform(matrix));
                DrawIconNodes(drawing, node, tint);
                drawing.Pop();
            }
            else if (node.Name.LocalName is "path" or "clip-path")
            {
                var geometry = Geometry.Parse((string?)node.Attribute("pathData") ?? "").Clone();
                if (geometry is StreamGeometry stream) stream.FillRule = (string?)node.Attribute("fillType") is "evenOdd" or "1" ? FillRule.EvenOdd : FillRule.Nonzero;
                if (node.Name.LocalName == "clip-path") { drawing.PushClip(geometry); clips++; continue; }
                var start = IconNumber(node, "trimPathStart", 0);
                var end = IconNumber(node, "trimPathEnd", 1);
                if (start != 0 || end != 1)
                    geometry = TrimIconPath(geometry, start, end, IconNumber(node, "trimPathOffset", 0));
                var stroke = IconBrush(node, "strokeColor", "strokeAlpha", tint);
                Pen? pen = stroke == null ? null : new Pen(stroke, IconNumber(node, "strokeWidth", 0));
                if (pen != null)
                {
                    pen.StartLineCap = pen.EndLineCap = (string?)node.Attribute("strokeLineCap") switch { "round" or "1" => PenLineCap.Round, "square" or "2" => PenLineCap.Square, _ => PenLineCap.Flat };
                    pen.LineJoin = (string?)node.Attribute("strokeLineJoin") switch { "round" or "1" => PenLineJoin.Round, "bevel" or "2" => PenLineJoin.Bevel, _ => PenLineJoin.Miter };
                }
                drawing.DrawGeometry(IconBrush(node, "fillColor", "fillAlpha", tint), pen, geometry);
            }
        }
        while (clips-- > 0) drawing.Pop();
    }

    static Geometry TrimIconPath(Geometry geometry, double start, double end, double offset)
    {
        var flat = geometry.GetFlattenedPathGeometry(0.001, ToleranceType.Absolute);
        var points = new List<Point>();
        // Android PathMeasure trims the first contour of a vector path.
        var figure = flat.Figures.FirstOrDefault();
        if (figure == null) return Geometry.Empty;
        points.Add(figure.StartPoint);
        foreach (var segment in figure.Segments)
        {
            if (segment is PolyLineSegment poly) points.AddRange(poly.Points);
            else if (segment is LineSegment line) points.Add(line.Point);
        }
        if (figure.IsClosed) points.Add(figure.StartPoint);
        var total = points.Zip(points.Skip(1), (a, b) => (b - a).Length).Sum();
        var first = ((start + offset) % 1 + 1) % 1 * total;
        var last = ((end + offset) % 1 + 1) % 1 * total;
        var result = new StreamGeometry();
        using (var context = result.Open())
        {
            void Append(double low, double high)
            {
                var distance = 0.0;
                var begun = false;
                for (var i = 1; i < points.Count; i++)
                {
                    var vector = points[i] - points[i - 1];
                    var length = vector.Length;
                    if (length > 0 && high > distance && low < distance + length)
                    {
                        var a = points[i - 1] + vector * Math.Clamp((low - distance) / length, 0, 1);
                        var b = points[i - 1] + vector * Math.Clamp((high - distance) / length, 0, 1);
                        if (!begun) { context.BeginFigure(a, false, false); begun = true; }
                        context.LineTo(b, true, false);
                    }
                    distance += length;
                }
            }
            if (first > last) { Append(first, total); Append(0, last); }
            else Append(first, last);
        }
        return result;
    }
}
