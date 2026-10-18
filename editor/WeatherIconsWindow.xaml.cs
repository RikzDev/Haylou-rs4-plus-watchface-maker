using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HaylouFaceEditor;

public partial class WeatherIconsWindow : Window
{
    readonly Func<WeatherIconSlot, string, BitmapSource> _defaultIcon;
    bool _ready;
    public ObservableCollection<WeatherIconRow> Rows { get; }
    public string SelectedStyle => (StyleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "fill";
    public bool RememberAsDefaults => RememberDefaultsBox.IsChecked == true;
    public bool AddToFace { get; private set; }

    public WeatherIconsWindow(string style, IEnumerable<WeatherIconRow> rows, Func<WeatherIconSlot, string, BitmapSource> defaultIcon)
    {
        _defaultIcon = defaultIcon;
        Rows = new(rows);
        InitializeComponent();
        IconList.ItemsSource = Rows;
        StyleBox.SelectedIndex = style == "outline" ? 1 : 0;
        IconList.SelectedIndex = 0;
        _ready = true;
        UpdateCount();
    }

    void UpdateCount() => IconCountText.Text = $"16 conditions / {Rows.Count(row => row.CustomFile != null)} custom images";

    void DefaultStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        foreach (var row in Rows.Where(row => row.CustomFile == null).ToArray()) ResetIcon(row.Key);
    }

    internal void ReplaceIcon(string key, string file)
    {
        var row = Rows.Single(row => row.Key == key);
        var preview = MainWindow.LoadBitmap(file); // Decode before replacing the row.
        var replacement = row with { Preview = preview, CustomFile = Path.GetFullPath(file) };
        Rows[Rows.IndexOf(row)] = replacement;
        IconList.SelectedItem = replacement;
        UpdateCount();
    }

    internal void ResetIcon(string key)
    {
        var row = Rows.Single(row => row.Key == key);
        var slot = MainWindow.WeatherSlots.Single(slot => slot.Key == key);
        var preview = _defaultIcon(slot, SelectedStyle);
        var replacement = row with { Preview = preview, CustomFile = null };
        var selected = ReferenceEquals(IconList.SelectedItem, row);
        Rows[Rows.IndexOf(row)] = replacement;
        if (selected) IconList.SelectedItem = replacement;
        UpdateCount();
    }

    void ReplaceIconClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WeatherIconRow row) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Replace " + row.Label,
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        try { ReplaceIcon(row.Key, dialog.FileName); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot load weather image", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    void ResetIconClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WeatherIconRow row) ResetIcon(row.Key);
    }
    void ResetAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.ToArray()) ResetIcon(row.Key);
    }
    void ApplyClick(object sender, RoutedEventArgs e) => Accept(false);
    void ApplyAndAddClick(object sender, RoutedEventArgs e) => Accept(true);
    void Accept(bool add)
    {
        try
        {
            foreach (var row in Rows.Where(row => row.CustomFile != null)) MainWindow.LoadBitmap(row.CustomFile!);
            AddToFace = add;
            DialogResult = true;
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot apply weather icons", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
