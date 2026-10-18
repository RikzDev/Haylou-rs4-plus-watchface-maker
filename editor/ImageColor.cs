using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class MainWindow
{
    void AnimationColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selectedAnimation == null) return;
        var color = AnimationColorBox.Text.Trim();
        if (color.Length != 0 && !ValidImageColor(color)) return;
        SetAnimationTint(_selectedAnimation, color.Length == 0 ? null : color);
        _savedBin = null;
        RefreshFrames(_selectedFrameIndex);
        Redraw();
    }

    static bool ValidImageColor(string text) => text.StartsWith('#') && text.Length is 7 or 9
        && text[1..].All(Uri.IsHexDigit);

    void ResetAnimationColorClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAnimation != null) AnimationColorBox.Text = "";
    }

    void SetAnimationTint(AnimationItem animation, string? color)
    {
        if (color == null)
        {
            if (animation.OriginalFrames.Count == animation.Frames.Count)
            {
                animation.Frames = animation.OriginalFrames.ToList();
                animation.Previews = animation.Frames.Select(LoadBitmap).ToList();
            }
            animation.OriginalFrames.Clear();
            animation.TintColor = null;
            return;
        }
        if (!ValidImageColor(color)) throw new ArgumentException("Use #RRGGBB or #AARRGGBB for the image color.");
        if (animation.OriginalFrames.Count == 0) animation.OriginalFrames = animation.Frames.ToList();
        if (animation.OriginalFrames.Count != animation.Frames.Count)
            throw new InvalidDataException("The original image frames do not match this sequence.");
        var tint = ColorOf(color);
        var frames = new List<string>();
        var previews = new List<BitmapSource>();
        for (var index = 0; index < animation.OriginalFrames.Count; index++)
        {
            var bitmap = TintBitmap(LoadBitmap(animation.OriginalFrames[index]), tint);
            var file = Path.Combine(_workingFolder, $"tint-{animation.AssetKey}-{index:D2}.png");
            SavePng(bitmap, file);
            frames.Add(file);
            previews.Add(bitmap);
        }
        animation.Frames = frames;
        animation.Previews = previews;
        animation.TintColor = color.ToUpperInvariant();
    }

    static BitmapSource TintBitmap(BitmapSource source, Color tint)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = tint.B;
            pixels[offset + 1] = tint.G;
            pixels[offset + 2] = tint.R;
            pixels[offset + 3] = (byte)((pixels[offset + 3] * tint.A + 127) / 255);
        }
        return BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Bgra32,
            null, pixels, bitmap.PixelWidth * 4);
    }
}
