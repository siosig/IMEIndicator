using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.ViewModels;

/// <summary>
/// マウスカーソルインジケーターのViewModel
/// </summary>
public partial class MouseCursorIndicatorViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private MouseCursorIndicatorSettings Settings => _settingsManager.Settings.MouseCursorIndicator;
    private LanguageInfo? _currentLanguageInfo;  // 現在の言語情報を保持

    [ObservableProperty]
    private double _size = 20;

    [ObservableProperty]
    private double _opacity = 0.9;

    [ObservableProperty]
    private string _displayText = "A";

    [ObservableProperty]
    private Brush _backgroundColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));

    [ObservableProperty]
    private double _fontSize = 10;

    [ObservableProperty]
    private FontFamily _fontFamily = new("Segoe UI");

    [ObservableProperty]
    private double _positionX;

    [ObservableProperty]
    private double _positionY;

    [ObservableProperty]
    private double _offsetX = 20;

    [ObservableProperty]
    private double _offsetY = 20;

    [ObservableProperty]
    private bool _isVisible = true;

    public MouseCursorIndicatorViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        Size = Settings.Size;
        Opacity = Settings.Opacity;
        OffsetX = Settings.OffsetX;
        OffsetY = Settings.OffsetY;
        IsVisible = Settings.IsVisible;
    }

    /// <summary>
    /// IME状態を更新する
    /// </summary>
    public void UpdateState(LanguageInfo languageInfo)
    {
        _currentLanguageInfo = languageInfo;  // 現在の状態を保持
        var imeSettings = _settingsManager.Settings.IMEIndicator;
        var languageKey = languageInfo.Language.ToString();
        LanguageColorSettings? activeSettings = null;

        // IME OFFの場合は英語表示
        if (!languageInfo.IsIMEOn && languageInfo.Language != LanguageType.English)
        {
            imeSettings.LanguageColors.TryGetValue("English", out activeSettings);
        }
        else
        {
            imeSettings.LanguageColors.TryGetValue(languageKey, out activeSettings);
        }

        if (activeSettings != null)
        {
            DisplayText = activeSettings.DisplayText ?? "?";
            var color = ParseColor(activeSettings.Color ?? "#3B82F6");
            BackgroundColor = new SolidColorBrush(color);

            // 言語別フォント設定（空ならグローバル設定を使用）
            var fontName = !string.IsNullOrEmpty(activeSettings.FontName) ? activeSettings.FontName : imeSettings.FontName;
            FontFamily = new FontFamily(fontName);

            // 言語別フォントサイズ（0ならグローバル設定を使用）
            var fontSizeRatio = activeSettings.FontSizeRatio > 0 ? activeSettings.FontSizeRatio : imeSettings.FontSizeRatio;
            FontSize = Size * fontSizeRatio;
        }
    }

    /// <summary>
    /// カーソル位置を更新
    /// </summary>
    public void UpdateCursorPosition(int x, int y)
    {
        PositionX = x + OffsetX;
        PositionY = y + OffsetY;
    }

    partial void OnSizeChanged(double value)
    {
        Settings.Size = value;
        // サイズ変更時にフォントサイズも更新
        var imeSettings = _settingsManager.Settings.IMEIndicator;
        FontSize = value * imeSettings.FontSizeRatio;
    }
    partial void OnOpacityChanged(double value) => Settings.Opacity = value;
    partial void OnOffsetXChanged(double value) => Settings.OffsetX = value;
    partial void OnOffsetYChanged(double value) => Settings.OffsetY = value;
    partial void OnIsVisibleChanged(bool value) => Settings.IsVisible = value;

    private static Color ParseColor(string hex)
    {
        try
        {
            if (hex.StartsWith("#")) hex = hex[1..];
            if (hex.Length == 6)
            {
                return Color.FromRgb(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
            }
        }
        catch { }
        return Colors.Gray;
    }

    /// <summary>
    /// 設定を再読み込みする
    /// </summary>
    public void ReloadSettings()
    {
        LoadSettings();
        // 現在のIME状態を再適用（フォントサイズ等を更新）
        if (_currentLanguageInfo != null)
        {
            UpdateState(_currentLanguageInfo);
        }
    }
}
