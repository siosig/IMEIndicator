using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.Views;

public partial class ColorPickerButton : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPickerButton),
            new FrameworkPropertyMetadata(Colors.Blue, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public static readonly DependencyProperty SelectedBrushProperty =
        DependencyProperty.Register(nameof(SelectedBrush), typeof(Brush), typeof(ColorPickerButton),
            new PropertyMetadata(new SolidColorBrush(Colors.Blue)));

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(nameof(ColorHex), typeof(string), typeof(ColorPickerButton),
            new PropertyMetadata("#0000FF"));

    public event EventHandler<Color>? ColorChanged;

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public Brush SelectedBrush
    {
        get => (Brush)GetValue(SelectedBrushProperty);
        set => SetValue(SelectedBrushProperty, value);
    }

    public string ColorHex
    {
        get => (string)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public ColorPickerButton()
    {
        InitializeComponent();
        UpdateFromColor(SelectedColor);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPickerButton picker && e.NewValue is Color color)
        {
            picker.UpdateFromColor(color);
            picker.ColorChanged?.Invoke(picker, color);
        }
    }

    private void UpdateFromColor(Color color)
    {
        SelectedBrush = new SolidColorBrush(color);
        ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DbgLog.Log(1, $"ColorPickerButton clicked: current color = {ColorHex}");
            var dialog = new ColorPickerDialog(SelectedColor);
            dialog.Owner = Window.GetWindow(this);
            DbgLog.Log(1, "ColorPickerDialog created, showing...");
            if (dialog.ShowDialog() == true)
            {
                DbgLog.Log(1, $"ColorPickerDialog OK: new color = #{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}");
                SelectedColor = dialog.SelectedColor;
            }
            else
            {
                DbgLog.Log(1, "ColorPickerDialog cancelled");
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "ColorPickerButton.Button_Click");
        }
    }
}
