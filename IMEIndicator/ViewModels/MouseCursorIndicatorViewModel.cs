using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IMEIndicator.Models;
using IMEIndicator.Services;

namespace IMEIndicator.ViewModels;

/// <summary>
/// マウスカーソルインジケーターのViewModel（日本語IME特化）
/// </summary>
public partial class MouseCursorIndicatorViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private MouseCursorIndicatorSettings Settings => _settingsManager.Settings.MouseCursorIndicator;
    private LanguageInfo? _currentLanguageInfo;

    private const double FontSizeRatio = 0.5;

    // Brush/FontFamily のキャッシュ（Freeze済みで再利用、アロケーションゼロ）
    private static readonly FontFamily MeiryoUiFont = new("Meiryo UI");
    private static readonly FontFamily SegoeUiFont = new("Segoe UI");
    private SolidColorBrush? _imeOnBgBrush;
    private SolidColorBrush? _imeOnGlowBrush;
    private SolidColorBrush? _imeOffBgBrush;
    private SolidColorBrush? _imeOffGlowBrush;
    private string? _cachedImeOnColor;
    private string? _cachedImeOffColor;

    [ObservableProperty]
    private double _size = 34;

    [ObservableProperty]
    private double _opacity = 0.9;

    [ObservableProperty]
    private string _displayText = "A";

    [ObservableProperty]
    private Brush _backgroundColor = CreateFrozenBrush(Color.FromRgb(59, 130, 246));

    [ObservableProperty]
    private Brush _glowColor = CreateFrozenBrush(Color.FromArgb(179, 59, 130, 246));

    [ObservableProperty]
    private double _fontSize = 17;

    [ObservableProperty]
    private FontFamily _fontFamily = SegoeUiFont;

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

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 設定の色文字列からFrozen Brushを生成・キャッシュする
    /// </summary>
    private void EnsureBrushCache()
    {
        var appSettings = _settingsManager.Settings;

        if (_cachedImeOnColor != appSettings.ImeOnColor || _imeOnBgBrush == null)
        {
            var color = ColorHelper.ParseColor(appSettings.ImeOnColor);
            _imeOnBgBrush = CreateFrozenBrush(color);
            _imeOnGlowBrush = CreateFrozenBrush(Color.FromArgb(179, color.R, color.G, color.B));
            _cachedImeOnColor = appSettings.ImeOnColor;
        }

        if (_cachedImeOffColor != appSettings.ImeOffColor || _imeOffBgBrush == null)
        {
            var color = ColorHelper.ParseColor(appSettings.ImeOffColor);
            _imeOffBgBrush = CreateFrozenBrush(color);
            _imeOffGlowBrush = CreateFrozenBrush(Color.FromArgb(179, color.R, color.G, color.B));
            _cachedImeOffColor = appSettings.ImeOffColor;
        }
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

        // Brushキャッシュを初期化
        EnsureBrushCache();

        // 初期表示をIME OFF状態に設定
        ApplyIMEColors(isIMEOn: false);
    }

    /// <summary>
    /// IME ON/OFF に応じた色とテキストを適用（キャッシュ済みBrushを再利用）
    /// </summary>
    private void ApplyIMEColors(bool isIMEOn)
    {
        EnsureBrushCache();
        var appSettings = _settingsManager.Settings;

        if (isIMEOn)
        {
            DisplayText = appSettings.ImeOnText;
            BackgroundColor = _imeOnBgBrush!;
            GlowColor = _imeOnGlowBrush!;
            FontFamily = MeiryoUiFont;
        }
        else
        {
            DisplayText = appSettings.ImeOffText;
            BackgroundColor = _imeOffBgBrush!;
            GlowColor = _imeOffGlowBrush!;
            FontFamily = SegoeUiFont;
        }
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
        // Brushキャッシュを強制再生成
        _cachedImeOnColor = null;
        _cachedImeOffColor = null;

        LoadSettings();
        if (_currentLanguageInfo.HasValue)
        {
            UpdateState(_currentLanguageInfo.Value);
        }
    }
}
