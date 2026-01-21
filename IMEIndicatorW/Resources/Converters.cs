using System.Globalization;
using System.Windows;
using System.Windows.Data;
using IMEIndicatorClock.Models;

namespace IMEIndicatorClock.Resources;

/// <summary>
/// フォントサイズを縮小するコンバーター
/// </summary>
public class FontSizeReducerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double fontSize)
        {
            return fontSize * 0.6;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 表示モードから時刻表示の可視性を判定するコンバーター
/// </summary>
public class DisplayModeToTimeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ClockDisplayMode mode)
        {
            return mode switch
            {
                ClockDisplayMode.TimeOnly => Visibility.Visible,
                ClockDisplayMode.DateAndTime => Visibility.Visible,
                ClockDisplayMode.TimeAndDate => Visibility.Visible,
                _ => Visibility.Collapsed
            };
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 表示モードから日付表示の可視性を判定するコンバーター
/// </summary>
public class DisplayModeToDateVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ClockDisplayMode mode)
        {
            return mode switch
            {
                ClockDisplayMode.DateOnly => Visibility.Visible,
                ClockDisplayMode.DateAndTime => Visibility.Visible,
                ClockDisplayMode.TimeAndDate => Visibility.Visible,
                _ => Visibility.Collapsed
            };
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 時計スタイルからデジタル表示の可視性を判定するコンバーター
/// </summary>
public class ClockStyleToDigitalVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ClockStyle style)
        {
            return style == ClockStyle.Digital ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 時計スタイルからアナログ表示の可視性を判定するコンバーター
/// </summary>
public class ClockStyleToAnalogVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ClockStyle style)
        {
            return style == ClockStyle.Analog ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// レイアウトから時刻表示の可視性を判定するコンバーター
/// </summary>
public class LayoutToTimeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTimeLayout layout)
        {
            return layout == DateTimeLayout.DateOnly
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// レイアウトから日付表示の可視性を判定するコンバーター
/// </summary>
public class LayoutToDateVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTimeLayout layout)
        {
            return layout == DateTimeLayout.TimeOnly
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// レイアウトからStackPanelの向きを判定するコンバーター
/// </summary>
public class LayoutToOrientationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTimeLayout layout)
        {
            return layout == DateTimeLayout.HorizontalDateFirst || layout == DateTimeLayout.HorizontalTimeFirst
                ? System.Windows.Controls.Orientation.Horizontal
                : System.Windows.Controls.Orientation.Vertical;
        }
        return System.Windows.Controls.Orientation.Vertical;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// レイアウトから日付を先に表示するかを判定するコンバーター（順序制御用）
/// </summary>
public class LayoutToDateFirstConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTimeLayout layout)
        {
            return layout == DateTimeLayout.VerticalDateFirst || layout == DateTimeLayout.HorizontalDateFirst;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// FontSizeからテキストのマージンを計算するコンバーター
/// parameter: "time" = 時刻用（下マージン負）, "date" = 日付用（上マージン負）
/// </summary>
public class FontSizeToTextMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double fontSize)
        {
            // フォントサイズの20%を負のマージンとして適用
            double margin = fontSize * 0.2;
            string param = parameter as string ?? "";

            if (param == "time")
            {
                // 時刻: 下マージンを負に
                return new Thickness(3, 0, 3, -margin);
            }
            else if (param == "date")
            {
                // 日付: 上マージンを負に
                return new Thickness(3, -margin, 3, 0);
            }
        }
        return new Thickness(3, 0, 3, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
