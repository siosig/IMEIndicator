using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IMEIndicatorClock.Views;

public partial class ColorPickerDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool _isUpdating;

    public Color SelectedColor { get; private set; }

    public Brush PreviewBrush { get; private set; } = new SolidColorBrush(Colors.Blue);

    public int RedValue { get; set; }
    public int GreenValue { get; set; }
    public int BlueValue { get; set; }
    public string HexValue { get; set; } = "#0000FF";

    private static readonly string[] PresetColorValues = new[]
    {
        "#3B82F6", "#EF4444", "#22C55E", "#F59E0B", "#8B5CF6",
        "#EC4899", "#06B6D4", "#84CC16", "#F97316", "#6366F1",
        "#14B8A6", "#A855F7", "#EAB308", "#DB2777", "#0EA5E9",
        "#000000", "#374151", "#6B7280", "#9CA3AF", "#FFFFFF"
    };

    public ColorPickerDialog(Color initialColor)
    {
        InitializeComponent();
        DataContext = this;

        SelectedColor = initialColor;
        RedValue = initialColor.R;
        GreenValue = initialColor.G;
        BlueValue = initialColor.B;
        UpdateHexFromRgb();
        UpdatePreview();

        CreatePresetColors();
    }

    private void CreatePresetColors()
    {
        foreach (var hex in PresetColorValues)
        {
            var color = ParseColor(hex);
            var rect = new Rectangle
            {
                Width = 25,
                Height = 25,
                Fill = new SolidColorBrush(color),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                RadiusX = 3,
                RadiusY = 3
            };
            rect.MouseLeftButtonDown += (s, e) =>
            {
                RedValue = color.R;
                GreenValue = color.G;
                BlueValue = color.B;
                UpdateHexFromRgb();
                UpdatePreview();
                UpdateBindings();
            };
            PresetColors.Children.Add(rect);
        }
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating) return;
        UpdateHexFromRgb();
        UpdatePreview();
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;
        UpdateHexFromRgb();
        UpdatePreview();
    }

    private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;
        var hex = HexTextBox.Text;
        if (hex.StartsWith("#") && hex.Length == 7)
        {
            try
            {
                var color = ParseColor(hex);
                _isUpdating = true;
                RedValue = color.R;
                GreenValue = color.G;
                BlueValue = color.B;
                UpdateBindings();
                _isUpdating = false;
                UpdatePreview();
            }
            catch { }
        }
    }

    private void UpdateHexFromRgb()
    {
        _isUpdating = true;
        HexValue = $"#{RedValue:X2}{GreenValue:X2}{BlueValue:X2}";
        if (HexTextBox != null)
        {
            HexTextBox.Text = HexValue;
        }
        _isUpdating = false;
    }

    private void UpdatePreview()
    {
        var color = Color.FromRgb((byte)RedValue, (byte)GreenValue, (byte)BlueValue);
        SelectedColor = color;
        PreviewBrush = new SolidColorBrush(color);
        OnPropertyChanged(nameof(PreviewBrush));
    }

    private void UpdateBindings()
    {
        OnPropertyChanged(nameof(RedValue));
        OnPropertyChanged(nameof(GreenValue));
        OnPropertyChanged(nameof(BlueValue));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static Color ParseColor(string hex)
    {
        if (hex.StartsWith("#"))
            hex = hex[1..];

        return Color.FromRgb(
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16)
        );
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
