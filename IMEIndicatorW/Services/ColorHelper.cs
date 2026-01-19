using System.Windows.Media;

namespace IMEIndicatorClock.Services;

/// <summary>
/// 色のパース・変換ヘルパー
/// </summary>
public static class ColorHelper
{
    /// <summary>
    /// HEX文字列からColorをパース
    /// 対応フォーマット: #RGB, #RGBA, #RRGGBB, #AARRGGBB
    /// </summary>
    public static Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Colors.Gray;

        try
        {
            if (hex.StartsWith("#"))
                hex = hex[1..];

            return hex.Length switch
            {
                // #RGB形式 (例: #F00 → 赤)
                3 => Color.FromRgb(
                    Convert.ToByte(new string(hex[0], 2), 16),
                    Convert.ToByte(new string(hex[1], 2), 16),
                    Convert.ToByte(new string(hex[2], 2), 16)),

                // #RGBA形式 (例: #F00F → 赤、不透明)
                4 => Color.FromArgb(
                    Convert.ToByte(new string(hex[3], 2), 16),
                    Convert.ToByte(new string(hex[0], 2), 16),
                    Convert.ToByte(new string(hex[1], 2), 16),
                    Convert.ToByte(new string(hex[2], 2), 16)),

                // #RRGGBB形式 (例: #FF0000 → 赤)
                6 => Color.FromRgb(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)),

                // #AARRGGBB形式 (例: #80FF0000 → 半透明の赤)
                8 => Color.FromArgb(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16)),

                _ => Colors.Gray
            };
        }
        catch (Exception ex)
        {
            DbgLog.W($"色の解析に失敗: {hex} - {ex.Message}");
            return Colors.Gray;
        }
    }

    /// <summary>
    /// ColorをARGB HEX文字列に変換 (#AARRGGBB)
    /// </summary>
    public static string ToArgbHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// ColorをRGB HEX文字列に変換 (#RRGGBB)
    /// </summary>
    public static string ToRgbHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// 有効なHEXカラー文字列か確認
    /// </summary>
    public static bool IsValidHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        if (hex.StartsWith("#"))
            hex = hex[1..];

        if (hex.Length != 3 && hex.Length != 4 && hex.Length != 6 && hex.Length != 8)
            return false;

        return hex.All(c => char.IsAsciiHexDigit(c));
    }
}
