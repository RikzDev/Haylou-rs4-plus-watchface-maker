using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HaylouFaceEditor;

public partial class MainWindow : Window
{
    const int ScreenWidth = 368;
    const int ScreenHeight = 448;
    // Prefer 253. The uploader can select another validated custom slot
    // when the firmware refuses replacement of an already installed ID.
    const int CustomDialId = 253;
    static readonly string WatchfaceRoot = FindWatchfaceRoot();

    readonly List<Widget> _widgets = new();
    readonly List<HandItem> _hands = new();
    readonly List<AnimationItem> _animations = new();
    readonly Dictionary<string, FontFamily> _fontFaces = new();
    readonly string _workingFolder = Path.Combine(Path.GetTempPath(), "rs4-editor-" + Guid.NewGuid().ToString("N"));
    Widget? _selected;
    AnimationItem? _selectedAnimation;
    int _selectedFrameIndex = -1;
    bool _filling;
    string? _fittedPng;
    string? _savedBin;
    int _animationTicks;
    string _animationPreviewMode = "live";
    bool _animationPreviewPlaying = true;
    readonly DispatcherTimer _previewTimer;

    public MainWindow()
    {
        Directory.CreateDirectory(_workingFolder);
        InitializeComponent();
        ResetFonts();
        NameBox.TextChanged += (_, _) => _savedBin = null;
        TitleBox.TextChanged += (_, _) => _savedBin = null;
        LoadDefaults();
        RefreshFontChoices();
        InitializeGlyphPreview();
        FillAnimationSources();
        AnimationPreviewModeBox.SelectedIndex = 0;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick += (_, _) => PreviewAnimations();
        _previewTimer.Start();
        Redraw();
    }

    void LoadDefaults()
    {
        _widgets.Clear();
        Add("hour", "Hora", 285, 185, "#CE8349", "12", 29, "hour");
        Add("colon", "Dois pontos", 304, 183, "#CE8349", ":", 29, "text");
        Add("minute", "Minuto", 322, 185, "#CE8349", "34", 29, "minute");
        Add("month", "Mês", 285, 214, "#CE8349", "10", 24, "month");
        Add("slash", "Barra", 305, 212, "#CE8349", "/", 24, "text");
        Add("day", "Dia", 322, 214, "#CE8349", "12", 24, "day");
        Add("heart", "Batimentos", 259, 270, "#B0C8A3", "72", 22, "heart");
        Add("steps", "Passos", 259, 299, "#B0C8A3", "1234", 22, "steps");
        Add("battery", "Bateria", 259, 325, "#B0C8A3", "15", 22, "battery");
        FillFunctions();
    }

    void FillFunctions()
    {
        FunctionBox.Items.Clear();
        foreach (var choice in Choices)
            FunctionBox.Items.Add(choice);
    }

    static readonly SourceChoice[] Choices =
    {
        new("Texto fixo", "text"),
        new("Hora", "hour"),
        new("Minuto", "minute"),
        new("Segundo", "second"),
        new("Mês", "month"),
        new("Dia", "day"),
        new("Dia da semana", "wday"),
        new("Passos", "steps"),
        new("Batimentos", "heart"),
        new("Bateria", "battery"),
        new("Temperatura (clima)", "temperature"),
        new("Valor ÷ 1000", "scaled"),
    };

    static readonly AnimationChoice[] AnimationChoices =
    {
        new("Imagem fixa", "static"),
        new("Loop contínuo", "loop"),
        new("Bateria", "battery"),
        new("Dia da semana", "weekday"),
        new("Dia do mês", "day"),
        new("Mês", "month"),
        new("Segundo", "second"),
        new("Minuto", "minute"),
        new("Hora (12h)", "hour"),
        new("Passos", "steps"),
        new("Batimentos", "heart"),
        new("Valor ÷ 1000", "scaled"),
    };

    void FillAnimationSources()
    {
        AnimationSourceBox.Items.Clear();
        foreach (var choice in AnimationChoices)
            AnimationSourceBox.Items.Add(choice);
    }

    void Add(string key, string label, int x, int y, string color, string sample, double size, string source)
    {
        _widgets.Add(new Widget
        {
            Key = key,
            Label = label,
            X = x,
            Y = y,
            Color = color,
            Sample = sample,
            Size = size,
            Source = source,
            FontFile = DefaultFontPath,
        });
    }

    void Redraw()
    {
        if (_canvasGesture != null)
            return;
        Overlay.Children.Clear();
        _shownFrames.Clear();
        foreach (var animation in _animations)
        {
            var frameIndex = PreviewFrameIndex(animation);
            if (frameIndex < 0 || frameIndex >= animation.Previews.Count)
                continue;
            var frame = animation.Previews[frameIndex];
            var offset = animation.OffsetAt(frameIndex);
            var image = new Image
            {
                Source = frame,
                Width = animation.Width,
                Height = animation.Height,
                IsHitTestVisible = false,
                Stretch = Stretch.Fill,
            };
            _shownFrames[animation] = frameIndex;
            AddCanvasItem(animation, image, animation.X + offset.X, animation.Y + offset.Y);
        }
        foreach (var hand in _hands)
        {
            var image = new Image
            {
                Source = hand.Preview,
                Width = hand.Width,
                Height = hand.Height,
                RenderTransformOrigin = new Point(hand.PivotFracX, hand.PivotFracY),
                RenderTransform = new RotateTransform(hand.PreviewDegrees),
                IsHitTestVisible = false,
            };
            AddCanvasItem(hand, image, hand.PosX, hand.PosY);
        }
        foreach (var widget in _widgets)
        {
            var border = new Border
            {
                Child = WidgetVisual(widget),
                Tag = widget,
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Cursor = Cursors.SizeAll,
            };
            border.MouseLeftButtonDown += BeginDrag;
            Canvas.SetLeft(border, widget.X);
            Canvas.SetTop(border, widget.Y);
            Overlay.Children.Add(border);
        }
        UpdateCanvasSelection();
        QueueGlyphPreview();
    }

    UIElement WidgetVisual(Widget widget)
    {
        if (_glyphPreviews.TryGetValue(widget, out var preview) && preview.Identity == GlyphIdentity(widget))
        {
            var previewScale = Math.Round(widget.Size) / preview.Size;
            return new Image { Source = preview.Image, Width = preview.Image.PixelWidth * previewScale,
                Height = preview.Image.PixelHeight * previewScale, Stretch = Stretch.Fill, IsHitTestVisible = false };
        }
        if (widget.CharacterPreviews.Count == 0)
            return new TextBlock
            {
                Text = widget.Sample,
                Foreground = Brush(widget.Color),
                FontFamily = FaceFont(widget.FontFile),
                FontSize = Math.Max(8, widget.Size),
            };
        var largest = widget.CharacterPreviews.Values.Max(image => Math.Max(image.PixelWidth, image.PixelHeight));
        var scale = widget.Size / largest;
        var width = widget.CharacterPreviews.Values.Max(image => Math.Max(1, (int)Math.Floor(image.PixelWidth * scale + 0.5)));
        var height = widget.CharacterPreviews.Values.Max(image => Math.Max(1, (int)Math.Floor(image.PixelHeight * scale + 0.5)));
        var pitch = Math.Max(1, width + widget.CharacterSpacing);
        var slots = widget.Source switch
        {
            "hour" or "minute" or "second" or "month" or "day" => 2,
            "heart" or "battery" => 3,
            "temperature" => 4,
            "steps" or "scaled" => 5,
            "wday" => 1,
            _ => Math.Clamp(widget.Sample.Length, 1, 12),
        };
        var sample = widget.Sample;
        if (sample.Length > slots)
            sample = sample[^slots..];
        sample = sample.PadLeft(slots, widget.Source is "hour" or "minute" or "second" or "month" or "day" ? '0' : ' ');
        var canvas = new Canvas { Width = width + (slots - 1) * pitch, Height = height, IsHitTestVisible = false };
        for (var index = 0; index < slots; index++)
        {
            if (widget.CharacterPreviews.TryGetValue(sample[index].ToString(), out var bitmap))
            {
                var image = new Image
                {
                    Source = bitmap,
                    Width = Math.Max(1, (int)Math.Floor(bitmap.PixelWidth * scale + 0.5)),
                    Height = Math.Max(1, (int)Math.Floor(bitmap.PixelHeight * scale + 0.5)),
                    Stretch = Stretch.Fill,
                };
                Canvas.SetLeft(image, index * pitch);
                canvas.Children.Add(image);
            }
            else if (sample[index] == '-')
            {
                var dash = new Border { Background = Brush(widget.Color), Width = Math.Max(1, width / 2), Height = Math.Max(1, height / 12) };
                Canvas.SetLeft(dash, index * pitch + width / 4);
                Canvas.SetTop(dash, height / 2);
                canvas.Children.Add(dash);
            }
        }
        return canvas;
    }

    void RefreshWidgetVisual()
    {
        foreach (var border in Overlay.Children.OfType<Border>())
            if (border.Tag is Widget widget && widget == _selected)
                border.Child = WidgetVisual(widget);
        UpdateCanvasSelection();
        QueueGlyphPreview();
    }

    void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag == null)
            return;
        if (border.Tag is Widget widget) Select(widget);
        else if (border.Tag is AnimationItem animation) ShowAnimation(animation);
        _canvasTarget = border.Tag;
        UpdateCanvasSelection();
        _pendingCanvasDrag = border.Tag;
        _pendingCanvasPoint = e.GetPosition(Overlay);
        _pendingWindowPoint = e.GetPosition(this);
        e.Handled = true;
    }

    void Select(Widget widget)
    {
        _selected = widget;
        _canvasTarget = widget;
        ShowSelection();
        UpdateCanvasSelection();
    }

    void ShowSelection()
    {
        _filling = true;
        ShowFontChoice();
        if (_selected == null)
        {
            SelectedName.Text = "nenhum";
            XBox.Text = "";
            YBox.Text = "";
            ColorBox.Text = "";
            SampleBox.Text = "";
            SizeBox.Text = "";
            FunctionBox.SelectedItem = null;
            CharacterInfo.Text = "";
            CharacterSpacingBox.Text = "0";
            CharacterSpacingBox.IsEnabled = false;
            ColorBox.IsEnabled = true;
        }
        else
        {
            SelectedName.Text = _selected.Label;
            XBox.Text = _selected.X.ToString();
            YBox.Text = _selected.Y.ToString();
            ColorBox.Text = _selected.Color;
            SampleBox.Text = _selected.Sample;
            SizeBox.Text = ((int)Math.Round(_selected.Size)).ToString();
            FunctionBox.SelectedItem = Choices.FirstOrDefault(choice => choice.Key == _selected.Source) ?? Choices[0];
            var images = _selected.CharacterImages.Count > 0;
            CharacterInfo.Text = images ? $"Caracteres em imagens: {_selected.CharacterImages.Count}. As cores originais são preservadas." : "Texto com fonte.";
            if (_selected.Source == "temperature")
                CharacterInfo.Text += " Clima sincronizado pelo celular; usa °C ou °F conforme o relógio. Sem dados: --.";
            CharacterSpacingBox.Text = _selected.CharacterSpacing.ToString();
            CharacterSpacingBox.IsEnabled = true;
            ColorBox.IsEnabled = !images;
        }
        _filling = false;
    }

    void CoordChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selected == null)
            return;
        if (int.TryParse(XBox.Text, out var x))
            _selected.X = Clamp(x, 0, ScreenWidth - 1);
        if (int.TryParse(YBox.Text, out var y))
            _selected.Y = Clamp(y, 0, ScreenHeight - 1);
        foreach (var child in Overlay.Children.OfType<Border>())
        {
            if (child.Tag != _selected)
                continue;
            Canvas.SetLeft(child, _selected.X);
            Canvas.SetTop(child, _selected.Y);
        }
        _savedBin = null;
        UpdateCanvasSelection();
    }

    void ColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selected == null || ColorBox.Text.Length != 7)
            return;
        try
        {
            _ = ColorOf(ColorBox.Text);
        }
        catch (FormatException)
        {
            return;
        }
        _selected.Color = ColorBox.Text.ToUpperInvariant();
        _savedBin = null;
        RefreshWidgetVisual();
    }

    void FontSizeChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selected == null)
            return;
        if (!int.TryParse(SizeBox.Text, out var size))
            return;
        _selected.Size = Math.Clamp(size, 8, 120);
        EnsureResizableFont(_selected);
        _savedBin = null;
        RefreshWidgetVisual();
    }

    void CharacterSpacingChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selected == null || !int.TryParse(CharacterSpacingBox.Text, out var spacing))
            return;
        _selected.CharacterSpacing = Math.Clamp(spacing, -120, 120);
        _savedBin = null;
        RefreshWidgetVisual();
    }

    void FunctionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected == null || FunctionBox.SelectedItem is not SourceChoice choice)
            return;
        _selected.Source = choice.Key;
        _savedBin = null;
        RefreshWidgetVisual();
    }

    void AddClick(object sender, RoutedEventArgs e)
    {
        var widget = new Widget
        {
            Key = "text" + (_widgets.Count + 1),
            Label = "Texto " + (_widgets.Count + 1),
            X = 24,
            Y = 24,
            Color = "#F3EDE4",
            Sample = "texto",
            Size = 24,
            Source = "text",
            FontFile = _selected?.FontFile ?? _availableFonts.FirstOrDefault(font => font.File != DefaultFontPath && font.File != null)?.File ?? DefaultFontPath,
        };
        _widgets.Add(widget);
        RefreshFontChoices();
        _savedBin = null;
        Redraw();
        Select(widget);
    }

    void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _widgets.Count <= 1)
            return;
        _widgets.Remove(_selected);
        _savedBin = null;
        _selected = _widgets[0];
        Redraw();
        Select(_selected);
    }

    void AddAnimationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imagens (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = true,
            Title = "Escolher os quadros da animação",
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length < 1)
        {
            Log("Escolha ao menos um quadro para a imagem ou sequência.");
            return;
        }
        var previews = dialog.FileNames.Select(LoadBitmap).ToList();
        _animations.Add(new AnimationItem
        {
            Label = "Animação " + (_animations.Count + 1),
            Frames = dialog.FileNames.ToList(),
            Previews = previews,
            FrameOffsets = previews.Select(_ => new FrameOffset()).ToList(),
            Width = previews[0].PixelWidth,
            Height = previews[0].PixelHeight,
            X = 24,
            Y = 24,
            Interval = 400,
            Source = "loop",
        });
        _savedBin = null;
        RefreshAnimations(_animations[^1]);
        Redraw();
    }

    void AddStaticImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imagens (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = false,
            Title = "Escolher imagem fixa",
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
            return;
        var preview = LoadBitmap(dialog.FileName);
        _animations.Add(new AnimationItem
        {
            Label = "Imagem fixa " + (_animations.Count + 1),
            Frames = new List<string> { dialog.FileName },
            Previews = new List<BitmapSource> { preview },
            FrameOffsets = new List<FrameOffset> { new() },
            Width = preview.PixelWidth,
            Height = preview.PixelHeight,
            X = 24,
            Y = 24,
            Source = "static",
        });
        _savedBin = null;
        RefreshAnimations(_animations[^1]);
        Redraw();
    }

    void RemoveAnimationClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAnimation == null)
            return;
        _animations.Remove(_selectedAnimation);
        _savedBin = null;
        RefreshAnimations(_animations.FirstOrDefault());
        Redraw();
    }

    void AnimationSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || AnimationList.SelectedItem is not AnimationItem animation)
            return;
        ShowAnimation(animation);
        _canvasTarget = animation;
        Redraw();
    }

    void AnimationChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selectedAnimation == null)
            return;
        if (int.TryParse(AnimationXBox.Text, out var x))
            _selectedAnimation.X = Clamp(x, 0, ScreenWidth - 1);
        if (int.TryParse(AnimationYBox.Text, out var y))
            _selectedAnimation.Y = Clamp(y, 0, ScreenHeight - 1);
        if (int.TryParse(AnimationWidthBox.Text, out var width))
            _selectedAnimation.Width = Clamp(width, 1, ScreenWidth);
        if (int.TryParse(AnimationHeightBox.Text, out var height))
            _selectedAnimation.Height = Clamp(height, 1, ScreenHeight);
        if (int.TryParse(AnimationIntervalBox.Text, out var interval))
            _selectedAnimation.Interval = Math.Clamp(interval, 200, 10000);
        if (AnimationSourceBox.SelectedItem is AnimationChoice choice)
            _selectedAnimation.Source = choice.Key;
        var label = AnimationNameBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(label))
            _selectedAnimation.Label = label;
        AnimationList.Items.Refresh();
        _savedBin = null;
        Redraw();
    }

    void RefreshAnimations(AnimationItem? selected = null)
    {
        AnimationList.ItemsSource = null;
        AnimationList.ItemsSource = _animations;
        AnimationInfo.Text = _animations.Count == 0
            ? "nenhuma"
            : $"{_animations.Count} item(ns)";
        ShowAnimation(selected);
    }

    void ShowAnimation(AnimationItem? animation)
    {
        _filling = true;
        _selectedAnimation = animation;
        if (animation != null)
            _canvasTarget = animation;
        AnimationList.SelectedItem = animation;
        if (animation == null)
        {
            AnimationNameBox.Text = "";
            AnimationXBox.Text = "";
            AnimationYBox.Text = "";
            AnimationWidthBox.Text = "";
            AnimationHeightBox.Text = "";
            AnimationIntervalBox.Text = "";
            AnimationSourceBox.SelectedItem = null;
        }
        else
        {
            AnimationNameBox.Text = animation.Label;
            AnimationXBox.Text = animation.X.ToString();
            AnimationYBox.Text = animation.Y.ToString();
            AnimationWidthBox.Text = animation.Width.ToString();
            AnimationHeightBox.Text = animation.Height.ToString();
            AnimationIntervalBox.Text = animation.Interval.ToString();
            AnimationSourceBox.SelectedItem = AnimationChoices.FirstOrDefault(item => item.Key == animation.Source);
        }
        _filling = false;
        RefreshFrames();
    }

    void FrameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
            return;
        _selectedFrameIndex = FrameList.SelectedIndex;
        _canvasTarget = _selectedAnimation;
        ShowFrameOffset();
        SetPreviewFrame(_selectedFrameIndex, true);
    }

    void AnimationPreviewModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || AnimationPreviewModeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
            return;
        _animationPreviewMode = mode;
        _animationPreviewPlaying = mode != "frame";
        UpdatePreviewControls();
        Redraw();
    }

    void AnimationPlayClick(object sender, RoutedEventArgs e)
    {
        _animationPreviewMode = "loop";
        _animationPreviewPlaying = !_animationPreviewPlaying;
        _filling = true;
        AnimationPreviewModeBox.SelectedIndex = 1;
        _filling = false;
        UpdatePreviewControls();
        Redraw();
    }

    void AnimationFrameSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling)
            return;
        SetPreviewFrame((int)Math.Round(e.NewValue), true);
    }

    void SetPreviewFrame(int index, bool selectFrame)
    {
        if (_selectedAnimation == null || _selectedAnimation.Frames.Count == 0)
            return;
        index = Math.Clamp(index, 0, _selectedAnimation.Frames.Count - 1);
        _animationPreviewMode = "frame";
        _animationPreviewPlaying = false;
        _filling = true;
        AnimationPreviewModeBox.SelectedIndex = 2;
        AnimationFrameSlider.Value = index;
        if (selectFrame)
            FrameList.SelectedIndex = index;
        _filling = false;
        _selectedFrameIndex = index;
        ShowFrameOffset();
        UpdatePreviewControls();
        Redraw();
    }

    int PreviewFrameIndex(AnimationItem animation)
    {
        if (animation != _selectedAnimation)
            return animation.FrameIndexAt(_animationTicks);
        if (_animationPreviewMode == "frame")
            return _selectedFrameIndex >= 0 ? _selectedFrameIndex : 0;
        if (_animationPreviewMode == "loop")
            return animation.LoopFrameIndexAt(_animationTicks);
        return animation.FrameIndexAt(_animationTicks);
    }

    void UpdatePreviewControls()
    {
        _filling = true;
        var count = _selectedAnimation?.Frames.Count ?? 0;
        AnimationFrameSlider.Maximum = Math.Max(0, count - 1);
        if (count == 0)
        {
            AnimationFrameSlider.Value = 0;
            AnimationFrameInfo.Text = "Selecione uma sequência";
        }
        else
        {
            var index = Math.Clamp(_selectedFrameIndex, 0, count - 1);
            AnimationFrameSlider.Value = index;
            AnimationFrameInfo.Text = _animationPreviewMode switch
            {
                "live" => $"Dados atuais · quadro {index + 1} de {count}",
                "loop" => _animationPreviewPlaying ? $"Reproduzindo · {count} quadros" : $"Ciclo pausado · quadro {index + 1} de {count}",
                _ => $"Quadro {index + 1} de {count}",
            };
        }
        AnimationPlayButton.Content = _animationPreviewPlaying ? "Pausar" : "Reproduzir";
        _filling = false;
    }

    void ShowFrameOffset()
    {
        _filling = true;
        if (_selectedAnimation == null || _selectedFrameIndex < 0)
        {
            FrameOffsetXBox.Text = "";
            FrameOffsetYBox.Text = "";
        }
        else
        {
            var offset = _selectedAnimation.OffsetAt(_selectedFrameIndex);
            FrameOffsetXBox.Text = offset.X.ToString();
            FrameOffsetYBox.Text = offset.Y.ToString();
        }
        _filling = false;
    }

    void FrameOffsetChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selectedAnimation == null || _selectedFrameIndex < 0)
            return;
        var offset = _selectedAnimation.OffsetAt(_selectedFrameIndex);
        if (int.TryParse(FrameOffsetXBox.Text, out var x))
            offset.X = Math.Clamp(x, -ScreenWidth, ScreenWidth);
        if (int.TryParse(FrameOffsetYBox.Text, out var y))
            offset.Y = Math.Clamp(y, -ScreenHeight, ScreenHeight);
        _savedBin = null;
        Redraw();
    }

    void RefreshFrames(int selected = -1)
    {
        _filling = true;
        if (_selectedAnimation == null)
        {
            FrameList.ItemsSource = null;
            _selectedFrameIndex = -1;
        }
        else
        {
            FrameList.ItemsSource = _selectedAnimation.Frames
                .Select((path, index) => new FrameView
                {
                    Label = $"{index + 1:D2}",
                    FileName = Path.GetFileName(path),
                    Preview = _selectedAnimation.Previews[index],
                })
                .ToList();
            var index = selected >= 0 ? selected : _selectedFrameIndex;
            FrameList.SelectedIndex = index >= 0 && index < _selectedAnimation.Frames.Count ? index : -1;
            _selectedFrameIndex = FrameList.SelectedIndex;
        }
        _filling = false;
        ShowFrameOffset();
        UpdatePreviewControls();
    }

    void AddFramesToAnimationClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAnimation == null)
        {
            AddAnimationClick(sender, e);
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imagens (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = true,
            Title = "Adicionar quadros à sequência",
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
            return;
        var index = _selectedFrameIndex < 0 ? _selectedAnimation.Frames.Count : _selectedFrameIndex + 1;
        foreach (var file in dialog.FileNames)
        {
            _selectedAnimation.Frames.Insert(index, file);
            _selectedAnimation.Previews.Insert(index, LoadBitmap(file));
            _selectedAnimation.FrameOffsets.Insert(index, new FrameOffset());
            index++;
        }
        _savedBin = null;
        RefreshFrames(index - 1);
        Redraw();
    }

    void RemoveFrameClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAnimation == null || _selectedFrameIndex < 0)
            return;
        if (_selectedAnimation.Frames.Count <= 1)
        {
            Log("A imagem precisa manter ao menos um quadro.");
            return;
        }
        _selectedAnimation.Frames.RemoveAt(_selectedFrameIndex);
        _selectedAnimation.Previews.RemoveAt(_selectedFrameIndex);
        _selectedAnimation.FrameOffsets.RemoveAt(_selectedFrameIndex);
        _savedBin = null;
        RefreshFrames(Math.Min(_selectedFrameIndex, _selectedAnimation.Frames.Count - 1));
        Redraw();
    }

    void MoveFrameUpClick(object sender, RoutedEventArgs e) => MoveFrame(-1);

    void MoveFrameDownClick(object sender, RoutedEventArgs e) => MoveFrame(1);

    void MoveFrame(int delta)
    {
        if (_selectedAnimation == null || _selectedFrameIndex < 0)
            return;
        var target = _selectedFrameIndex + delta;
        if (target < 0 || target >= _selectedAnimation.Frames.Count)
            return;
        (_selectedAnimation.Frames[_selectedFrameIndex], _selectedAnimation.Frames[target]) =
            (_selectedAnimation.Frames[target], _selectedAnimation.Frames[_selectedFrameIndex]);
        (_selectedAnimation.Previews[_selectedFrameIndex], _selectedAnimation.Previews[target]) =
            (_selectedAnimation.Previews[target], _selectedAnimation.Previews[_selectedFrameIndex]);
        (_selectedAnimation.FrameOffsets[_selectedFrameIndex], _selectedAnimation.FrameOffsets[target]) =
            (_selectedAnimation.FrameOffsets[target], _selectedAnimation.FrameOffsets[_selectedFrameIndex]);
        _savedBin = null;
        RefreshFrames(target);
        Redraw();
    }

    void PreviewAnimations()
    {
        if (_animations.Count == 0 || _canvasGesture != null)
            return;
        if (_animationPreviewPlaying)
            _animationTicks++;
        Redraw();
    }

    void SampleChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _selected == null)
            return;
        _selected.Sample = SampleBox.Text;
        _savedBin = null;
        RefreshWidgetVisual();
    }

    void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "TBWF (*.tbwf)|*.tbwf",
            Title = "Importar TBWF",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            ImportTbwf(dialog.FileName);
            Log($"Importei {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Log("erro: " + ex.Message);
        }
    }

    void ImportTbwf(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        ImportFonts(zip);
        var jsonEntry = zip.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith("TiBoWaFe.json", StringComparison.OrdinalIgnoreCase));
        var pngEntry = zip.Entries.FirstOrDefault(entry =>
            entry.FullName.Replace('\\', '/').EndsWith("/resourceImage/dial.png", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith("dial.png", StringComparison.OrdinalIgnoreCase));
        if (jsonEntry == null || pngEntry == null)
            throw new InvalidDataException("o tbwf não tem TiBoWaFe.json e dial.png");
        using var jsonStream = jsonEntry.Open();
        using var reader = new StreamReader(jsonStream);
        var document = JsonDocument.Parse(reader.ReadToEnd());
        JsonElement elements = default;
        var hasElements = document.RootElement.TryGetProperty("elements", out elements) && elements.ValueKind == JsonValueKind.Object;
        _animations.Clear();
        if (hasElements)
            ImportElements(zip, elements);
        using var pngStream = pngEntry.Open();
        using var pngMemory = new MemoryStream();
        pngStream.CopyTo(pngMemory);
        pngMemory.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = pngMemory;
        bitmap.EndInit();
        var fitted = FitCover(bitmap);
        BackgroundImage.Source = fitted;
        _fittedPng = Path.Combine(_workingFolder, "background.png");
        SavePng(fitted, _fittedPng);
        _hands.Clear();
        if (hasElements)
        {
            ImportHands(zip, elements, bitmap.PixelWidth, bitmap.PixelHeight);
            var claimedImages = ImportAnimations(zip, elements);
            ImportUnreferencedFixedImages(zip, claimedImages);
        }
        if (_widgets.Count == 0)
            Add("day", "Dia", 160, 300, "#FFFFFF", "12", 24, "day");
        var ratio = bitmap.PixelWidth / (double)bitmap.PixelHeight;
        if (Math.Abs(ratio - (ScreenWidth / (double)ScreenHeight)) > 0.02)
            Log($"Esse desenho é {bitmap.PixelWidth}x{bitmap.PixelHeight}, não é o formato do RS4. Vou encaixar em 368x448.");
        else if (bitmap.PixelWidth != ScreenWidth)
            Log($"Reduzi {bitmap.PixelWidth}x{bitmap.PixelHeight} para 368x448.");
        _savedBin = null;
        RefreshFontChoices();
        RefreshAnimations(_animations.FirstOrDefault());
        Redraw();
        if (_selected != null)
            Select(_selected);
    }

    void ImportElements(ZipArchive zip, JsonElement elements)
    {
        var imported = new List<Widget>();
        var fonts = new HashSet<string>();
        var characterSets = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var characterFolder = Path.Combine(Path.GetTempPath(), "rs4-characters-" + Guid.NewGuid().ToString("N"));
        foreach (var property in elements.EnumerateObject())
        {
            var element = property.Value;
            if (element.ValueKind != JsonValueKind.Object)
                continue;
            if (property.Name is "dial" || property.Name.EndsWith("Resource", StringComparison.Ordinal) || property.Name.EndsWith("Hand", StringComparison.Ordinal) || HasVisualResource(element))
                continue;
            if (element.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False)
                continue;
            if (!element.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number)
                continue;
            if (!element.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number)
                continue;
            var source = SourceFor(property.Name);
            var characters = ExtractCharacters(zip, element, characterSets, characterFolder);
            var fontFile = characters.Count == 0 ? ExtractFont(zip, element) ?? DefaultFontPath : null;
            if (fontFile != null)
                fonts.Add(Path.GetFileName(fontFile));
            imported.Add(new Widget
            {
                Key = property.Name,
                Label = property.Name,
                X = Clamp((int)Math.Round(x.GetDouble() * ScreenWidth), 0, ScreenWidth - 1),
                Y = Clamp((int)Math.Round(y.GetDouble() * ScreenHeight), 0, ScreenHeight - 1),
                Color = ReadColor(element),
                Sample = SampleFor(source, property.Name),
                Size = characters.Count > 0 ? ReadCharacterSize(element) : ReadSize(element),
                Source = source,
                FontFile = fontFile,
                CharacterImages = characters,
                CharacterPreviews = characters.ToDictionary(pair => pair.Key, pair => LoadBitmap(pair.Value)),
            });
            if (characters.Count > 0)
                Log($"{property.Name}: {characters.Count} caracteres em imagens, função {source}.");
        }
        if (fonts.Count > 0)
            Log("Fonte " + string.Join(", ", fonts) + ". Vou desenhar os números e as letras que o texto usa.");
        _widgets.Clear();
        _widgets.AddRange(imported);
        _selected = _widgets.FirstOrDefault();
    }

    static double ReadCharacterSize(JsonElement element)
    {
        var resource = element.GetProperty("resourceCharacter");
        return Math.Clamp(Fraction(resource, "size", 24.0 / ScreenWidth) * ScreenWidth, 8, 120);
    }

    static Dictionary<string, string> ExtractCharacters(ZipArchive zip, JsonElement element,
        Dictionary<string, Dictionary<string, string>> cache, string folder)
    {
        if (!element.TryGetProperty("resourceCharacter", out var resource) || resource.ValueKind != JsonValueKind.Object)
            return new();
        var path = resource.TryGetProperty("resource", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("resourceCharacter sem caminho de imagens.");
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (cache.TryGetValue(normalized, out var existing))
            return existing;
        var entries = zip.Entries.Where(entry =>
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            return entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && (normalized.EndsWith('/') ? name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                    : name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        if (entries.Count == 0)
            throw new InvalidDataException($"Não achei as imagens de caracteres em {path}.");
        Directory.CreateDirectory(folder);
        var result = new Dictionary<string, string>();
        void AddCharacter(string character, BitmapSource image)
        {
            if (result.ContainsKey(character))
                throw new InvalidDataException($"Caractere duplicado {character} em {path}.");
            var destination = Path.Combine(folder, $"s{cache.Count:D2}_c{(int)character[0]:D3}.png");
            SavePng(image, destination);
            result.Add(character, destination);
        }
        var sections = resource.TryGetProperty("sections", out var sectionsValue) ? sectionsValue.GetInt32() : 1;
        if (entries.Count == 1 && sections > 1)
        {
            if (sections != 10)
                throw new InvalidDataException("Faixas de caracteres precisam conter os 10 dígitos em ordem 0–9.");
            var frames = SplitFrames(ReadZipBitmap(entries[0]), sections);
            for (var index = 0; index < frames.Count; index++)
                AddCharacter(index.ToString(), frames[index]);
        }
        else
        {
            foreach (var entry in entries)
            {
                var character = CharacterForFile(entry.Name);
                if (character == null)
                    throw new InvalidDataException($"Nome de caractere não reconhecido: {entry.Name}.");
                AddCharacter(character, ReadZipBitmap(entry));
            }
        }
        cache.Add(normalized, result);
        return result;
    }

    static string? CharacterForFile(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name).TrimStart('_').ToLowerInvariant();
        if (stem.Length == 1 && stem[0] < 128)
            return stem;
        return stem switch
        {
            "zero" => "0", "one" => "1", "two" => "2", "three" => "3", "four" => "4",
            "five" => "5", "six" => "6", "seven" => "7", "eight" => "8", "nine" => "9",
            "slash" => "/", "colon" => ":", "minus" or "dash" or "hyphen" => "-",
            "percent" => "%", "dot" or "point" => ".", "space" => " ",
            _ => null,
        };
    }

    static bool HasVisualResource(JsonElement element)
    {
        foreach (var name in new[] { "resourceImage", "resourceIcon" })
        {
            if (!element.TryGetProperty(name, out var resource) || resource.ValueKind != JsonValueKind.Object)
                continue;
            if (resource.TryGetProperty("resource", out var path)
                && path.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(path.GetString()))
                return true;
        }
        return false;
    }

    HashSet<string> ImportAnimations(ZipArchive zip, JsonElement elements)
    {
        var claimedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in elements.EnumerateObject())
        {
            var element = property.Value;
            if (property.Name == "dial" || element.ValueKind != JsonValueKind.Object || HandKind(property.Name) != null)
                continue;
            if (element.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False)
                continue;
            JsonElement resource = default;
            if (element.TryGetProperty("resourceImage", out var image) && image.ValueKind == JsonValueKind.Object)
                resource = image;
            else if (element.TryGetProperty("resourceIcon", out var icon) && icon.ValueKind == JsonValueKind.Object)
                resource = icon;
            else
                continue;
            var sections = resource.TryGetProperty("sections", out var sectionValue) && sectionValue.ValueKind == JsonValueKind.Number
                ? sectionValue.GetInt32()
                : 1;
            var resourcePath = resource.TryGetProperty("resource", out var pathValue) ? pathValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(resourcePath))
                continue;
            var normalized = resourcePath.Replace('\\', '/').TrimStart('/');
            var entries = normalized.EndsWith("/", StringComparison.Ordinal)
                ? zip.Entries.Where(entry => entry.FullName.Replace('\\', '/').TrimStart('/').StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList()
                : zip.Entries.Where(entry => entry.FullName.Replace('\\', '/').TrimStart('/').EndsWith(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
            entries = OrderAnimationEntries(property.Name, entries);
            if (entries.Count == 0)
            {
                Log($"A animação {property.Name} usa {resourcePath}, mas os quadros não estão no TBWF.");
                continue;
            }
            foreach (var entry in entries)
                claimedImages.Add(entry.FullName.Replace('\\', '/').TrimStart('/'));
            var rawFrames = new List<BitmapSource>();
            foreach (var entry in entries)
            {
                var bitmap = ReadZipBitmap(entry);
                if (entries.Count == 1 && sections > 1)
                    rawFrames.AddRange(SplitFrames(bitmap, sections));
                else
                    rawFrames.Add(bitmap);
            }
            if (rawFrames.Count < 1)
                continue;
            var size = Math.Max(0.05, Fraction(resource, "size", 0.2));
            var target = Math.Max(1, (int)Math.Round(size * ScreenWidth));
            var frames = new List<string>();
            var previews = new List<BitmapSource>();
            for (var index = 0; index < rawFrames.Count; index++)
            {
                var raw = rawFrames[index];
                var scale = target / (double)Math.Max(raw.PixelWidth, raw.PixelHeight);
                var scaled = Scale(raw, scale);
                var safe = string.Concat(property.Name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
                var file = Path.Combine(_workingFolder, $"animation-{safe}-{index:D2}.png");
                SavePng(scaled, file);
                frames.Add(file);
                previews.Add(scaled);
            }
            _animations.Add(new AnimationItem
            {
                Label = property.Name,
                Frames = frames,
                Previews = previews,
                FrameOffsets = previews.Select(_ => new FrameOffset()).ToList(),
                X = Clamp((int)Math.Round(Fraction(element, "x", 0) * ScreenWidth), 0, ScreenWidth - 1),
                Y = Clamp((int)Math.Round(Fraction(element, "y", 0) * ScreenHeight), 0, ScreenHeight - 1),
                Width = previews[0].PixelWidth,
                Height = previews[0].PixelHeight,
                Interval = 400,
                Source = property.Name switch
                {
                    "battery" => "battery",
                    "dayOfWeek" => "weekday",
                    _ when frames.Count == 1 => "static",
                    _ => "loop",
                },
            });
            Log($"Animação {property.Name}: {frames.Count} quadros.");
        }
        return claimedImages;
    }

    void ImportUnreferencedFixedImages(ZipArchive zip, HashSet<string> claimedImages)
    {
        var images = zip.Entries
            .Where(entry => entry.FullName.StartsWith("resourceImage/", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !claimedImages.Contains(entry.FullName.Replace('\\', '/').TrimStart('/'))
                && !entry.Name.Equals("dial.png", StringComparison.OrdinalIgnoreCase)
                && !entry.Name.Contains("Hand", StringComparison.OrdinalIgnoreCase))
            .ToList();
        for (var index = 0; index < images.Count; index++)
        {
            var source = ReadZipBitmap(images[index]);
            var scale = Math.Min(1, 160 / (double)Math.Max(source.PixelWidth, source.PixelHeight));
            var preview = Scale(source, scale);
            var safe = string.Concat(images[index].Name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            var file = Path.Combine(_workingFolder, $"fixed-{safe}-{index:D2}.png");
            SavePng(preview, file);
            _animations.Add(new AnimationItem
            {
                Label = Path.GetFileNameWithoutExtension(images[index].Name) + " (fixa)",
                Frames = new List<string> { file },
                Previews = new List<BitmapSource> { preview },
                FrameOffsets = new List<FrameOffset> { new() },
                X = 24 + index * 12,
                Y = 24 + index * 12,
                Width = preview.PixelWidth,
                Height = preview.PixelHeight,
                Source = "static",
            });
        }
        if (images.Count > 0)
            Log($"Importei {images.Count} imagem(ns) fixa(s) sem elemento no TBWF.");
    }

    static List<ZipArchiveEntry> OrderAnimationEntries(string name, List<ZipArchiveEntry> entries)
    {
        var days = new[] { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };
        if (name == "dayOfWeek")
            return entries.OrderBy(entry => Array.FindIndex(days, day => entry.Name.StartsWith(day, StringComparison.OrdinalIgnoreCase))).ToList();
        return entries.OrderBy(entry => FrameOrder(entry.Name)).ThenBy(entry => entry.Name).ToList();
    }

    static int FrameOrder(string name)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    static BitmapSource ReadZipBitmap(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        return CopyPixels(bitmap);
    }

    static List<BitmapSource> SplitFrames(BitmapSource source, int sections)
    {
        var vertical = source.PixelHeight >= source.PixelWidth;
        var width = vertical ? source.PixelWidth : source.PixelWidth / sections;
        var height = vertical ? source.PixelHeight / sections : source.PixelHeight;
        var frames = new List<BitmapSource>();
        for (var index = 0; index < sections; index++)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var sourceRect = vertical
                    ? new Int32Rect(0, index * height, width, height)
                    : new Int32Rect(index * width, 0, width, height);
                context.DrawImage(new CroppedBitmap(source, sourceRect), new Rect(0, 0, width, height));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            frames.Add(CopyPixels(bitmap));
        }
        return frames;
    }

    static string SourceFor(string name) => name switch
    {
        "hour" => "hour",
        "minute" => "minute",
        "second" => "second",
        "month" => "month",
        "day" => "day",
        "dayOfWeek" => "wday",
        "step" => "steps",
        "heartRate" => "heart",
        "battery" => "battery",
        "weatherTemperature" => "temperature",
        "distance" => "scaled",
        _ => "text",
    };

    static string SampleFor(string source, string name) => source switch
    {
        "hour" => "12",
        "minute" => "34",
        "second" => "56",
        "month" => "10",
        "day" => "12",
        "wday" => "1",
        "steps" => "1234",
        "heart" => "72",
        "battery" => "15",
        "temperature" => "25",
        "scaled" => "3",
        _ => name is "timeSeparator" ? ":" : name is "dateSeparator" ? "/" : name is "batteryPercentSign" ? "%"
            : name is "weatherDescription" ? "--" : name,
    };

    static string ReadColor(JsonElement element)
    {
        if (!element.TryGetProperty("color", out var color))
            return "#FFFFFF";
        if (color.ValueKind == JsonValueKind.Number)
            return Hex(color.GetInt32());
        if (color.ValueKind == JsonValueKind.Object
            && color.TryGetProperty("colors", out var colors)
            && colors.ValueKind == JsonValueKind.Array
            && colors.GetArrayLength() > 0
            && colors[0].ValueKind == JsonValueKind.Number)
            return Hex(colors[0].GetInt32());
        return "#FFFFFF";
    }

    string? ExtractFont(ZipArchive zip, JsonElement element)
    {
        if (!element.TryGetProperty("font", out var font) || font.ValueKind != JsonValueKind.Object)
            return null;
        if (!font.TryGetProperty("family", out var family) || family.ValueKind != JsonValueKind.String)
            return null;
        var name = (family.GetString() ?? "").Replace('\\', '/').TrimStart('/');
        if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            return null;
        if (_zipFonts.TryGetValue(name, out var path)) return path;
        return _zipFonts.FirstOrDefault(pair => pair.Key.EndsWith("/" + Path.GetFileName(name), StringComparison.OrdinalIgnoreCase)).Value;
    }

    FontFamily FaceFont(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new FontFamily("Consolas");
        if (_fontFaces.TryGetValue(path, out var cached))
            return cached;
        try
        {
            var uri = new Uri(path);
            var typeface = new GlyphTypeface(uri);
            var family = typeface.FamilyNames.Values.FirstOrDefault()
                ?? Path.GetFileNameWithoutExtension(path);
            var face = new FontFamily(uri, "./#" + family);
            _fontFaces[path] = face;
            return face;
        }
        catch
        {
            return new FontFamily("Consolas");
        }
    }

    static double ReadSize(JsonElement element)
    {
        if (!element.TryGetProperty("font", out var font))
            return 24;
        if (font.ValueKind == JsonValueKind.Number)
            return Math.Clamp(font.GetDouble() * ScreenHeight, 8, 120);
        if (font.ValueKind == JsonValueKind.Object
            && font.TryGetProperty("size", out var size)
            && size.ValueKind == JsonValueKind.Number)
            return Math.Clamp(size.GetDouble() * ScreenHeight, 8, 120);
        return 24;
    }

    void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Projeto (*.hface.json)|*.hface.json",
            Title = "Abrir projeto",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            LoadProject(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log("erro: " + ex.Message);
        }
    }

    void LoadProject(string path)
    {
        var project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("projeto vazio");
        ResetFonts();
        foreach (var saved in project.Fonts)
        {
            var file = Path.Combine(Path.GetDirectoryName(path)!, saved.File);
            if (File.Exists(file)) RegisterFont(file, saved.Label);
        }
        NameBox.Text = project.Name;
        TitleBox.Text = project.Title;
        _widgets.Clear();
        foreach (var saved in project.Widgets)
        {
            var widget = new Widget { Key = saved.Key, Label = string.IsNullOrWhiteSpace(saved.Label) ? saved.Key : saved.Label };
            widget.X = saved.X;
            widget.Y = saved.Y;
            widget.Color = saved.Color;
            widget.Sample = saved.Sample;
            widget.Size = saved.Size <= 0 ? 24 : saved.Size;
            widget.FontFile = DefaultFontPath;
            if (!string.IsNullOrWhiteSpace(saved.Source))
                widget.Source = saved.Source;
            if (!string.IsNullOrWhiteSpace(saved.Font))
            {
                var fontPath = Path.Combine(Path.GetDirectoryName(path)!, saved.Font);
                widget.FontFile = File.Exists(fontPath) ? fontPath : DefaultFontPath;
            }
            widget.CharacterSpacing = saved.CharacterSpacing;
            foreach (var pair in saved.Characters)
            {
                var characterPath = Path.Combine(Path.GetDirectoryName(path)!, pair.Value);
                if (!File.Exists(characterPath))
                    throw new InvalidDataException($"Imagem do caractere {pair.Key} não encontrada: {characterPath}");
                widget.CharacterImages.Add(pair.Key, characterPath);
                widget.CharacterPreviews.Add(pair.Key, LoadBitmap(characterPath));
            }
            _widgets.Add(widget);
        }
        _selected = _widgets.FirstOrDefault();
        _hands.Clear();
        foreach (var saved in project.Hands)
        {
            var handFile = Path.Combine(Path.GetDirectoryName(path)!, saved.File);
            if (!File.Exists(handFile))
                throw new InvalidDataException($"Imagem do ponteiro não encontrada: {handFile}");
            var bitmap = LoadBitmap(handFile);
            _hands.Add(new HandItem
            {
                Kind = saved.Kind, File = handFile, Preview = bitmap,
                Width = saved.Width > 0 ? saved.Width : bitmap.PixelWidth,
                Height = saved.Height > 0 ? saved.Height : bitmap.PixelHeight,
                PivotX = saved.PivotX, PivotY = saved.PivotY,
                PivotFracX = saved.PivotFracX, PivotFracY = saved.PivotFracY,
                PosX = saved.PosX, PosY = saved.PosY, PreviewDegrees = saved.PreviewDegrees,
            });
        }
        var png = Path.Combine(Path.GetDirectoryName(path)!, project.Background);
        if (File.Exists(png))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(png);
            bitmap.EndInit();
            BackgroundImage.Source = bitmap;
            _fittedPng = png;
        }
        _animations.Clear();
        foreach (var saved in project.Animations)
        {
            var frames = saved.Frames
                .Select(frame => Path.Combine(Path.GetDirectoryName(path)!, frame))
                .Where(File.Exists)
                .ToList();
            if (frames.Count < 1)
                continue;
            var previews = frames.Select(LoadBitmap).ToList();
            _animations.Add(new AnimationItem
            {
                Label = saved.Label,
                Frames = frames,
                Previews = previews,
                X = saved.X,
                Y = saved.Y,
                Width = saved.Width > 0 ? saved.Width : previews[0].PixelWidth,
                Height = saved.Height > 0 ? saved.Height : previews[0].PixelHeight,
                Interval = Math.Clamp(saved.Interval, 200, 10000),
                Source = AnimationChoices.Any(choice => choice.Key == saved.Source) ? saved.Source : "loop",
                FrameOffsets = frames.Select((_, index) => index < saved.FrameOffsets.Count
                    ? saved.FrameOffsets[index]
                    : new FrameOffset()).ToList(),
            });
        }
        _savedBin = null;
        RefreshFontChoices();
        RefreshAnimations(_animations.FirstOrDefault());
        Redraw();
        ShowSelection();
        Log($"Abri {path}");
    }

    void SaveProjectClick(object sender, RoutedEventArgs e)
    {
        if (_fittedPng == null)
        {
            Log("Importa um TBWF antes de salvar o projeto.");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Projeto (*.hface.json)|*.hface.json",
            FileName = NameBox.Text + ".hface.json",
        };
        if (dialog.ShowDialog() != true)
            return;
        SaveProject(dialog.FileName);
    }

    void SaveProject(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var pngName = Path.GetFileNameWithoutExtension(path) + ".png";
        var backgroundDest = Path.Combine(folder, pngName);
        if (!Path.GetFullPath(_fittedPng!).Equals(Path.GetFullPath(backgroundDest), StringComparison.OrdinalIgnoreCase))
            File.Copy(_fittedPng!, backgroundDest, true);
        RefreshFontChoices();
        var savedFontPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var savedFonts = new List<SavedFont>();
        var fontFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-fonts");
        foreach (var font in _availableFonts.Where(font => font.File != null &&
            (font.File != DefaultFontPath || _widgets.Any(widget => widget.FontFile == font.File))))
        {
            if (!File.Exists(font.File))
                continue;
            Directory.CreateDirectory(fontFolder);
            var absoluteFont = Path.GetFullPath(font.File!);
            var existingRelative = Path.GetRelativePath(fontFolder, absoluteFont);
            var fontDest = !Path.IsPathRooted(existingRelative) && !existingRelative.StartsWith("..")
                ? absoluteFont : Path.Combine(fontFolder, Guid.NewGuid().ToString("N") + Path.GetExtension(font.File));
            if (!Path.GetFullPath(font.File!).Equals(Path.GetFullPath(fontDest), StringComparison.OrdinalIgnoreCase))
                File.Copy(font.File!, fontDest, true);
            var relative = Path.GetRelativePath(folder, fontDest);
            savedFontPaths[font.File!] = relative;
            savedFonts.Add(new SavedFont { Label = font.Label, File = relative });
        }
        var characterFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-characters");
        var savedCharacters = new List<Dictionary<string, string>>();
        for (var index = 0; index < _widgets.Count; index++)
        {
            var characters = new Dictionary<string, string>();
            foreach (var pair in _widgets[index].CharacterImages)
            {
                Directory.CreateDirectory(characterFolder);
                var filename = $"w{index:D2}_c{(int)pair.Key[0]:D3}.png";
                var destination = Path.Combine(characterFolder, filename);
                if (!Path.GetFullPath(pair.Value).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    File.Copy(pair.Value, destination, true);
                characters.Add(pair.Key, Path.Combine(Path.GetFileName(characterFolder), filename));
            }
            savedCharacters.Add(characters);
        }
        var frameFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-frames");
        Directory.CreateDirectory(frameFolder);
        var savedAnimations = new List<SavedAnimation>();
        for (var animationIndex = 0; animationIndex < _animations.Count; animationIndex++)
        {
            var animation = _animations[animationIndex];
            var savedFrames = new List<string>();
            var savedOffsets = new List<FrameOffset>();
            for (var frameIndex = 0; frameIndex < animation.Frames.Count; frameIndex++)
            {
                var source = animation.Frames[frameIndex];
                if (!File.Exists(source))
                    continue;
                var name = $"a{animationIndex:D2}_{frameIndex:D2}.png";
                var destination = Path.Combine(frameFolder, name);
                if (!Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    File.Copy(source, destination, true);
                savedFrames.Add(Path.Combine(Path.GetFileName(frameFolder), name));
                savedOffsets.Add(animation.OffsetAt(frameIndex));
            }
            if (savedFrames.Count >= 1)
            {
                savedAnimations.Add(new SavedAnimation
                {
                    Label = animation.Label,
                    Frames = savedFrames,
                    FrameOffsets = savedOffsets,
                    X = animation.X,
                    Y = animation.Y,
                    Width = animation.Width,
                    Height = animation.Height,
                    Interval = animation.Interval,
                    Source = animation.Source,
                });
            }
        }
        var savedHands = _hands.Select((hand, index) =>
        {
            var filename = $"hand{index:D2}_{hand.Kind}.png";
            var destination = Path.Combine(frameFolder, filename);
            if (!Path.GetFullPath(hand.File).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Copy(hand.File, destination, true);
            return new SavedHand
            {
                Kind = hand.Kind, File = Path.Combine(Path.GetFileName(frameFolder), filename),
                Width = hand.Width, Height = hand.Height, PivotX = hand.PivotX, PivotY = hand.PivotY,
                PivotFracX = hand.PivotFracX, PivotFracY = hand.PivotFracY,
                PosX = hand.PosX, PosY = hand.PosY, PreviewDegrees = hand.PreviewDegrees,
            };
        }).ToList();
        var project = new ProjectFile
        {
            Id = ReadId(),
            Name = NameBox.Text.Trim(),
            Title = TitleBox.Text.Trim(),
            Background = pngName,
            Animations = savedAnimations,
            Hands = savedHands,
            Fonts = savedFonts,
            Widgets = _widgets.Select((widget, index) => new SavedWidget
            {
                Key = widget.Key,
                Label = widget.Label,
                X = widget.X,
                Y = widget.Y,
                Color = widget.Color,
                Sample = widget.Sample,
                Size = widget.Size,
                Source = widget.Source,
                Font = string.IsNullOrEmpty(widget.FontFile) ? "" : savedFontPaths.GetValueOrDefault(widget.FontFile, ""),
                Characters = savedCharacters[index],
                CharacterSpacing = widget.CharacterSpacing,
            }).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        _fittedPng = Path.Combine(folder, pngName);
        Log($"Projeto salvo em {path}");
    }

    async void SaveBinClick(object sender, RoutedEventArgs e)
    {
        if (_fittedPng == null)
        {
            Log("Importa um TBWF antes de gravar o bin.");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Watchface (*.bin)|*.bin",
            FileName = NameBox.Text.Trim() + ".bin",
            InitialDirectory = Path.Combine(WatchfaceRoot, "out"),
        };
        if (dialog.ShowDialog() != true)
            return;
        await RunBuild(dialog.FileName);
    }

    async void SendClick(object sender, RoutedEventArgs e)
    {
        if (!await EnsureBin())
            return;
        var answer = MessageBox.Show(
            "O Bluetooth do celular precisa estar desligado. O ID inicial é " + ReadId() +
            ". Se estiver ocupado, o envio usa outro ID válido e preserva a face instalada.",
            "Enviar",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
            return;
        var source = Path.Combine(WatchfaceRoot, "send_face2.py");
        if (!File.Exists(source))
        {
            Log("Não achei o script de envio.");
            return;
        }
        await RunTool(Python(), $"\"{source}\" \"{_savedBin}\" {ReadId()}", "Envio");
    }

    async void RecoveryClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Isso é o reset de fábrica do Haylou Fun. Apaga as faces, o pareamento e as configurações do relógio. O Bluetooth do celular precisa estar desligado. Quando a escuta começar, reinicia o relógio.",
            "Recovery",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
            return;
        var script = Path.Combine(WatchfaceRoot, "factory_reset.py");
        if (!File.Exists(script))
        {
            Log("Não achei o script de recovery.");
            return;
        }
        await RunTool(Python(), $"\"{script}\"", "Recovery");
    }

    async Task<bool> RunBuild(string output)
    {
        var layout = Path.Combine(_workingFolder, "layout.json");
        var payload = new
        {
            widgets = _widgets.Select(widget => new
            {
                key = widget.Key,
                x = widget.X,
                y = widget.Y,
                color = widget.Color,
                size = (int)Math.Round(widget.Size),
                source = widget.Source,
                text = widget.Sample,
                font = widget.FontFile ?? DefaultFontPath,
                characters = widget.CharacterImages,
                spacing = widget.CharacterSpacing,
            }),
            hands = _hands.Select(hand => new
            {
                kind = hand.Kind,
                file = hand.File,
                width = hand.Width,
                height = hand.Height,
                pivotx = hand.PivotX,
                pivoty = hand.PivotY,
                posx = hand.PosX,
                posy = hand.PosY,
            }),
            animations = _animations.Select(animation => new
            {
                frames = animation.Frames,
                x = animation.X,
                y = animation.Y,
                width = animation.Width,
                height = animation.Height,
                interval = animation.Interval,
                source = animation.Source,
                offsets = animation.Frames.Select((_, index) =>
                {
                    var offset = animation.OffsetAt(index);
                    return new { x = offset.X, y = offset.Y };
                }).ToList(),
            }),
        };
        await File.WriteAllTextAsync(layout, JsonSerializer.Serialize(payload));
        var script = Path.Combine(WatchfaceRoot, "build_live.py");
        var args = $"\"{script}\" --png \"{_fittedPng}\" --layout \"{layout}\" --id {ReadId()} --name \"{NameBox.Text.Trim()}\" --title \"{TitleBox.Text.Trim()}\" -o \"{output}\"";
        if (await RunTool(Python(), args, "Montagem"))
        {
            _savedBin = output;
            Log("Bin pronto.");
            return true;
        }
        return false;
    }

    async Task<bool> EnsureBin()
    {
        if (_savedBin != null && File.Exists(_savedBin))
        {
            var builtAt = File.GetLastWriteTimeUtc(_savedBin);
            var sources = new[] { "build_live.py", "module/clock.c", "module/imports.c" };
            if (sources.Any(source => File.GetLastWriteTimeUtc(Path.Combine(WatchfaceRoot, source)) > builtAt))
            {
                Log("O gerador foi atualizado. Recriando o bin antes do envio.");
                return await RunBuild(_savedBin);
            }
            return true;
        }
        if (_fittedPng == null)
        {
            Log("Importa um TBWF antes de enviar.");
            return false;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Watchface (*.bin)|*.bin",
            FileName = NameBox.Text.Trim() + ".bin",
            InitialDirectory = Path.Combine(WatchfaceRoot, "out"),
        };
        if (dialog.ShowDialog() != true)
            return false;
        return await RunBuild(dialog.FileName);
    }

    async Task<bool> RunTool(string program, string arguments, string title)
    {
        SetBusy(true);
        Log("");
        Log(title);
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = program,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += (_, args) => { if (args.Data != null) DispatchLog(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) DispatchLog(args.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Log($"Falhou com código {process.ExitCode}.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log("erro: " + ex.Message);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    void DispatchLog(string line)
    {
        Dispatcher.Invoke(() => Log(line));
    }

    void SetBusy(bool busy)
    {
        ImportButton.IsEnabled = !busy;
        OpenButton.IsEnabled = !busy;
        SaveProjectButton.IsEnabled = !busy;
        SaveBinButton.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        RecoveryButton.IsEnabled = !busy;
        AddButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy;
        AddAnimationButton.IsEnabled = !busy;
        AddStaticImageButton.IsEnabled = !busy;
        RemoveAnimationButton.IsEnabled = !busy;
        AddFrameButton.IsEnabled = !busy;
        RemoveFrameButton.IsEnabled = !busy;
        MoveFrameUpButton.IsEnabled = !busy;
        MoveFrameDownButton.IsEnabled = !busy;
    }

    int ReadId()
    {
        return CustomDialId;
    }

    void Log(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

    static string Python() => FindPython();

    static Color ColorOf(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush(ColorOf(hex));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    static string Hex(int argb)
    {
        uint value = (uint)argb;
        return $"#{(value >> 16) & 255:X2}{(value >> 8) & 255:X2}{value & 255:X2}";
    }

    void ImportHands(ZipArchive zip, JsonElement elements, int designW, int designH)
    {
        double cover = Math.Max(ScreenWidth / (double)designW, ScreenHeight / (double)designH);
        double originX = (ScreenWidth - designW * cover) / 2;
        double originY = (ScreenHeight - designH * cover) / 2;
        foreach (var property in elements.EnumerateObject())
        {
            var kind = HandKind(property.Name);
            var element = property.Value;
            if (kind == null || element.ValueKind != JsonValueKind.Object)
                continue;
            if (element.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False)
                continue;
            if (!element.TryGetProperty("resourceImage", out var image) || image.ValueKind != JsonValueKind.Object)
                continue;
            var sections = image.TryGetProperty("sections", out var sectionValue) && sectionValue.ValueKind == JsonValueKind.Number
                ? sectionValue.GetInt32()
                : 1;
            if (sections > 1)
            {
                Log($"{property.Name} tem {sections} quadros. Essa faixa ainda não entra no bin.");
                continue;
            }
            var resource = image.TryGetProperty("resource", out var resourceValue) ? resourceValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(resource))
                continue;
            var needle = resource.Replace('\\', '/').TrimStart('/');
            var entry = zip.Entries.FirstOrDefault(item =>
                item.FullName.Replace('\\', '/').EndsWith(needle, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                Log($"Não achei a imagem {resource}.");
                continue;
            }
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = memory;
            source.EndInit();
            double handSize = Fraction(image, "size", 1);
            if (handSize <= 0)
                handSize = 1;
            double aspect = source.PixelWidth / (double)source.PixelHeight;
            int targetHeight;
            int targetWidth;
            if (aspect >= 1)
            {
                targetWidth = Math.Max(1, (int)Math.Round(handSize * ScreenWidth));
                targetHeight = Math.Max(1, (int)Math.Round(targetWidth / aspect));
            }
            else
            {
                targetHeight = Math.Max(1, (int)Math.Round(handSize * ScreenWidth));
                targetWidth = Math.Max(1, (int)Math.Round(targetHeight * aspect));
            }
            var scaled = Scale(source, targetHeight / (double)source.PixelHeight);
            double pivotFracX = Fraction(image, "pivotX", 0.5);
            double pivotFracY = Fraction(image, "pivotY", 0);
            if (pivotFracX < 0 || pivotFracX > 1)
                pivotFracX = 0.5;
            // Hello Haylou rotates around the bottom hub: 1 + pivotY/size.
            // A value already between 0 and 1 is a direct fraction of the image.
            if (pivotFracY < 0 || pivotFracY > 1)
                pivotFracY = Math.Clamp(1 + pivotFracY / handSize, 0, 1);
            int pivotX = (int)Math.Round(scaled.PixelWidth * pivotFracX);
            int pivotY = (int)Math.Round(scaled.PixelHeight * pivotFracY);
            double fx = Fraction(element, "x", 0.5);
            double fy = Fraction(element, "y", 0.5);
            int posX = (int)Math.Round(fx * designW * cover + originX - pivotX);
            int posY = (int)Math.Round(fy * designH * cover + originY - pivotY);
            var file = Path.Combine(_workingFolder, $"hand-{kind}.png");
            SavePng(scaled, file);
            _hands.Add(new HandItem
            {
                Kind = kind,
                File = file,
                Preview = scaled,
                Width = scaled.PixelWidth,
                Height = scaled.PixelHeight,
                PivotX = pivotX,
                PivotY = pivotY,
                PivotFracX = pivotFracX,
                PivotFracY = pivotFracY,
                PosX = posX,
                PosY = posY,
                PreviewDegrees = kind switch
                {
                    "hour" => 305,
                    "minute" => 63,
                    _ => 180,
                },
            });
            Log($"Ponteiro {kind}, {scaled.PixelWidth}x{scaled.PixelHeight}.");
        }
    }

    static string? HandKind(string name) => name switch
    {
        "hourHand" => "hour",
        "minuteHand" => "minute",
        "secondHand" => "second",
        _ => null,
    };

    static double Fraction(JsonElement element, string name, double fallback)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;
    }

    static BitmapSource Scale(BitmapSource source, double scale)
    {
        int width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        int height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawImage(source, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return CopyPixels(bitmap);
    }

    static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        return CopyPixels(bitmap);
    }

    static BitmapSource FitCover(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            double scale = Math.Max(ScreenWidth / (double)source.PixelWidth, ScreenHeight / (double)source.PixelHeight);
            double width = source.PixelWidth * scale;
            double height = source.PixelHeight * scale;
            context.DrawImage(source, new Rect((ScreenWidth - width) / 2, (ScreenHeight - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(ScreenWidth, ScreenHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return CopyPixels(bitmap);
    }

    static BitmapSource CopyPixels(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        if (copy.CanFreeze)
            copy.Freeze();
        return copy;
    }

    static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CopyPixels(source)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

public sealed class Widget
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public string Sample { get; set; } = "";
    public double Size { get; set; } = 24;
    public string Source { get; set; } = "text";
    public string? FontFile { get; set; }
    public Dictionary<string, string> CharacterImages { get; set; } = new();
    public Dictionary<string, BitmapSource> CharacterPreviews { get; set; } = new();
    public int CharacterSpacing { get; set; }
}

public sealed class ProjectFile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Background { get; set; } = "";
    public List<SavedWidget> Widgets { get; set; } = new();
    public List<SavedAnimation> Animations { get; set; } = new();
    public List<SavedHand> Hands { get; set; } = new();
    public List<SavedFont> Fonts { get; set; } = new();
}

public sealed class SavedFont
{
    public string Label { get; set; } = "";
    public string File { get; set; } = "";
}

public sealed class SavedHand
{
    public string Kind { get; set; } = "";
    public string File { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int PivotX { get; set; }
    public int PivotY { get; set; }
    public double PivotFracX { get; set; }
    public double PivotFracY { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public double PreviewDegrees { get; set; }
}

public sealed class SavedWidget
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public string Sample { get; set; } = "";
    public double Size { get; set; }
    public string Source { get; set; } = "";
    public string Font { get; set; } = "";
    public Dictionary<string, string> Characters { get; set; } = new();
    public int CharacterSpacing { get; set; }
}

public sealed class HandItem
{
    public string Kind { get; set; } = "";
    public string File { get; set; } = "";
    public BitmapSource? Preview { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int PivotX { get; set; }
    public int PivotY { get; set; }
    public double PivotFracX { get; set; }
    public double PivotFracY { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public double PreviewDegrees { get; set; }
}

public sealed class AnimationItem
{
    public string Label { get; set; } = "Animação";
    public List<string> Frames { get; set; } = new();
    public List<BitmapSource> Previews { get; set; } = new();
    public List<FrameOffset> FrameOffsets { get; set; } = new();
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Interval { get; set; } = 400;
    public string Source { get; set; } = "loop";

    public FrameOffset OffsetAt(int index)
    {
        while (FrameOffsets.Count < Frames.Count)
            FrameOffsets.Add(new FrameOffset());
        return index >= 0 && index < FrameOffsets.Count ? FrameOffsets[index] : new FrameOffset();
    }

    public int FrameIndexAt(int ticks)
    {
        if (Previews.Count == 0)
            return -1;
        var now = DateTime.Now;
        var value = Source switch
        {
            "static" => 0,
            "battery" => 15 * Previews.Count / 101,
            "weekday" => (int)now.DayOfWeek,
            "day" => now.Day - 1,
            "month" => now.Month - 1,
            "second" => now.Second,
            "minute" => now.Minute,
            "hour" => now.Hour % 12,
            "steps" => 1234,
            "heart" => 72,
            "scaled" => 3,
            _ => -1,
        };
        if (value >= 0)
            return value % Previews.Count;
        return LoopFrameIndexAt(ticks);
    }

    public int LoopFrameIndexAt(int ticks)
    {
        if (Previews.Count == 0)
            return -1;
        var period = Math.Max(1, (Interval + 199) / 200);
        return (ticks / period) % Previews.Count;
    }
}

public sealed class FrameView
{
    public string Label { get; set; } = "";
    public string FileName { get; set; } = "";
    public BitmapSource? Preview { get; set; }
}

public sealed class FrameOffset
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class SavedAnimation
{
    public string Label { get; set; } = "Animação";
    public List<string> Frames { get; set; } = new();
    public List<FrameOffset> FrameOffsets { get; set; } = new();
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Interval { get; set; } = 400;
    public string Source { get; set; } = "loop";
}

public sealed class SourceChoice
{
    public SourceChoice(string label, string key)
    {
        Label = label;
        Key = key;
    }

    public string Label { get; }
    public string Key { get; }
    public override string ToString() => Label;
}

public sealed class AnimationChoice
{
    public AnimationChoice(string label, string key)
    {
        Label = label;
        Key = key;
    }

    public string Label { get; }
    public string Key { get; }
    public override string ToString() => Label;
}
