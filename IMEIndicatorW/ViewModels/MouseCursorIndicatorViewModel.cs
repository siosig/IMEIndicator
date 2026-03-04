using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.ViewModels;

/// <summary>
/// マウスカーソルインジケーターのViewModel（日本語IME特化）
/// </summary>
public partial class MouseCursorIndicatorViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private MouseCursorIndicatorSettings Settings => _settingsManager.Settings.MouseCursorIndicator;
    private LanguageInfo? _currentLanguageInfo;

    private const double FontSizeRatio = 0.5;

    [ObservableProperty]
    private double _size = 34;

    [ObservableProperty]
    private double _opacity = 0.9;

    [ObservableProperty]
    private string _displayText = "A";

    [ObservableProperty]
    private Brush _backgroundColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));

    [ObservableProperty]
    private Brush _glowColor = new SolidColorBrush(Color.FromArgb(179, 59, 130, 246));

    [ObservableProperty]
    private double _fontSize = 17;

    [ObservableProperty]
    private FontFamily _fontFamily = new("Segoe UI");

    [ObservableProperty]
    private double _positionX;

    [ObservableProperty]
    private double _positionY;

    [ObservableProperty]
    private double _offsetX = 15;

    [ObservableProperty]
    private double _offsetY = 15;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _hideWhenImeOff = true;

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
        HideWhenImeOff = Settings.HideWhenImeOff;
        FontSize = Size * FontSizeRatio;

        // 初期表示をIME OFF状態に設定
        ApplyIMEColors(isIMEOn: false);
    }

    /// <summary>
    /// IME ON/OFF に応じた色とテキストを適用
    /// </summary>
    private void ApplyIMEColors(bool isIMEOn)
    {
        var appSettings = _settingsManager.Settings;

        if (isIMEOn)
        {
            DisplayText = appSettings.ImeOnText;
            var color = ColorHelper.ParseColor(appSettings.ImeOnColor);
            BackgroundColor = new SolidColorBrush(color);
            GlowColor = new SolidColorBrush(Color.FromArgb(179, color.R, color.G, color.B));
        }
        else
        {
            DisplayText = appSettings.ImeOffText;
            var color = ColorHelper.ParseColor(appSettings.ImeOffColor);
            BackgroundColor = new SolidColorBrush(color);
            GlowColor = new SolidColorBrush(Color.FromArgb(179, color.R, color.G, color.B));
        }

        // 日本語テキストにはMeiryo UIを使用
        FontFamily = isIMEOn ? new FontFamily("Meiryo UI") : new FontFamily("Segoe UI");
    }

    /// <summary>
    /// IME状態を更新する
    /// </summary>
    public void UpdateState(LanguageInfo languageInfo)
    {
        _currentLanguageInfo = languageInfo;

        // 日本語IME ON以外はすべてIME OFFとして扱う
        bool isIMEOn = languageInfo.Language == LanguageType.Japanese && languageInfo.IsIMEOn;
        ApplyIMEColors(isIMEOn);
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
        FontSize = value * FontSizeRatio;
    }

    partial void OnOpacityChanged(double value) => Settings.Opacity = value;
    partial void OnOffsetXChanged(double value) => Settings.OffsetX = value;
    partial void OnOffsetYChanged(double value) => Settings.OffsetY = value;
    partial void OnIsVisibleChanged(bool value) => Settings.IsVisible = value;
    partial void OnHideWhenImeOffChanged(bool value) => Settings.HideWhenImeOff = value;

    /// <summary>
    /// 設定を再読み込みする
    /// </summary>
    public void ReloadSettings()
    {
        LoadSettings();
        if (_currentLanguageInfo != null)
        {
            UpdateState(_currentLanguageInfo);
        }
    }
}
