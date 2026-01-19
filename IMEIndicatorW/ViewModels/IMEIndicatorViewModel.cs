using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.ViewModels;

/// <summary>
/// IMEインジケーターのViewModel
/// </summary>
public partial class IMEIndicatorViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private IMEIndicatorSettings Settings => _settingsManager.Settings.IMEIndicator;

    [ObservableProperty]
    private double _size = 60;

    [ObservableProperty]
    private double _opacity = 0.9;

    [ObservableProperty]
    private string _displayText = "A";

    [ObservableProperty]
    private Brush _backgroundColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));

    [ObservableProperty]
    private Brush _glowColor = new SolidColorBrush(Color.FromArgb(128, 59, 130, 246));

    [ObservableProperty]
    private double _fontSize = 30;

    [ObservableProperty]
    private FontFamily _fontFamily = new("Segoe UI");

    public string FontName
    {
        get => Settings.FontName;
        set
        {
            Settings.FontName = value;
            FontFamily = new FontFamily(value);
        }
    }

    public double FontSizeRatio
    {
        get => Settings.FontSizeRatio;
        set
        {
            Settings.FontSizeRatio = value;
            UpdateFontSize();
        }
    }

    [ObservableProperty]
    private double _positionX = 100;

    [ObservableProperty]
    private double _positionY = 100;

    [ObservableProperty]
    private LanguageType _currentLanguage = LanguageType.English;

    [ObservableProperty]
    private bool _isIMEOn;

    [ObservableProperty]
    private int _displayIndex;

    public IMEIndicatorViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        Size = Settings.Size;
        Opacity = Settings.Opacity;
        FontFamily = new FontFamily(Settings.FontName ?? "Segoe UI");
        DisplayIndex = Settings.DisplayIndex;
        UpdateFontSize();

        // 位置が-1の場合は画面右下に自動配置（初回起動時）
        if (Settings.PositionX < 0 || Settings.PositionY < 0)
        {
            InitializeDefaultPosition();
        }
        else
        {
            PositionX = Settings.PositionX;
            PositionY = Settings.PositionY;
            // 位置の境界チェック
            EnsurePositionWithinBounds();
        }

        // 初期状態は英語
        UpdateState(new LanguageInfo(LanguageType.English, false));
    }

    /// <summary>
    /// 初回起動時のデフォルト位置（画面左上）を設定
    /// </summary>
    private void InitializeDefaultPosition()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        int displayIndex = DisplayIndex;
        if (displayIndex >= screens.Length) displayIndex = 0;
        var screen = screens[displayIndex].WorkingArea;

        const double offset = 50;
        PositionX = screen.Left + offset;
        PositionY = screen.Top + offset;

        // 設定に保存
        Settings.PositionX = PositionX;
        Settings.PositionY = PositionY;
    }

    /// <summary>
    /// IME状態を更新する
    /// </summary>
    public void UpdateState(LanguageInfo languageInfo)
    {
        DbgLog.Log(5, $"[IMEIndicatorVM] UpdateState: {languageInfo.Language}/{languageInfo.IsIMEOn}");
        CurrentLanguage = languageInfo.Language;
        IsIMEOn = languageInfo.IsIMEOn;

        var languageColors = Settings.LanguageColors;
        if (languageColors == null || languageColors.Count == 0)
        {
            DbgLog.Log(5, "[IMEIndicatorVM] LanguageColors is null/empty, using default");
            // デフォルト表示
            DisplayText = "A";
            BackgroundColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            GlowColor = new SolidColorBrush(Color.FromArgb(128, 59, 130, 246));
            return;
        }

        var languageKey = languageInfo.Language.ToString();
        LanguageColorSettings? activeSettings = null;

        // IME OFFの場合は英語表示
        if (!languageInfo.IsIMEOn && languageInfo.Language != LanguageType.English)
        {
            languageColors.TryGetValue("English", out activeSettings);
            DbgLog.Log(5, $"[IMEIndicatorVM] IME OFF -> English settings: {(activeSettings != null ? "found" : "not found")}");
        }
        else
        {
            languageColors.TryGetValue(languageKey, out activeSettings);
            DbgLog.Log(5, $"[IMEIndicatorVM] {languageKey} settings: {(activeSettings != null ? "found" : "not found")}");
        }

        if (activeSettings != null)
        {
            var color = ColorHelper.ParseColor(activeSettings.Color ?? "#3B82F6");
            DbgLog.Log(5, $"[IMEIndicatorVM] 設定適用: Text={activeSettings.DisplayText}, Color={activeSettings.Color}");
            DisplayText = activeSettings.DisplayText ?? "?";
            BackgroundColor = new SolidColorBrush(color);
            GlowColor = new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B));

            // 言語別フォント設定（空ならグローバル設定を使用）
            var fontName = !string.IsNullOrEmpty(activeSettings.FontName) ? activeSettings.FontName : Settings.FontName;
            FontFamily = new FontFamily(fontName);

            // 言語別フォントサイズ（0ならグローバル設定を使用）
            var fontSizeRatio = activeSettings.FontSizeRatio > 0 ? activeSettings.FontSizeRatio : Settings.FontSizeRatio;
            FontSize = Size * fontSizeRatio;
        }
        else
        {
            DbgLog.Log(5, "[IMEIndicatorVM] activeSettings is null, no update");
        }
    }

    partial void OnSizeChanged(double value)
    {
        Settings.Size = value;
        UpdateFontSize();
    }

    partial void OnOpacityChanged(double value)
    {
        Settings.Opacity = value;
    }

    partial void OnPositionXChanged(double value)
    {
        Settings.PositionX = value;
    }

    partial void OnPositionYChanged(double value)
    {
        Settings.PositionY = value;
    }

    partial void OnFontFamilyChanged(FontFamily value)
    {
        Settings.FontName = value.Source;
    }

    partial void OnDisplayIndexChanged(int value)
    {
        Settings.DisplayIndex = value;
    }

    private void UpdateFontSize()
    {
        FontSize = Size * Settings.FontSizeRatio;
    }

    /// <summary>
    /// ウィンドウ位置が画面内に収まるように調整
    /// </summary>
    private void EnsurePositionWithinBounds()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        // 現在のディスプレイを取得
        int displayIndex = DisplayIndex;
        if (displayIndex >= screens.Length) displayIndex = 0;
        var screen = screens[displayIndex].WorkingArea;

        const double margin = 10;

        // X座標の境界チェック
        double maxX = screen.Right - Size - margin;
        double minX = screen.Left + margin;
        if (PositionX > maxX) PositionX = maxX;
        if (PositionX < minX) PositionX = minX;

        // Y座標の境界チェック
        double maxY = screen.Bottom - Size - margin;
        double minY = screen.Top + margin;
        if (PositionY > maxY) PositionY = maxY;
        if (PositionY < minY) PositionY = minY;
    }

    /// <summary>
    /// 言語色設定を取得
    /// </summary>
    public Dictionary<string, LanguageColorSettings> GetLanguageColors()
    {
        return Settings.LanguageColors;
    }

    /// <summary>
    /// 言語色設定を更新
    /// </summary>
    public void SetLanguageColor(string languageKey, string colorHex, string displayText)
    {
        if (Settings.LanguageColors.ContainsKey(languageKey))
        {
            var existing = Settings.LanguageColors[languageKey];
            Settings.LanguageColors[languageKey] = new LanguageColorSettings
            {
                Color = colorHex,
                DisplayText = displayText,
                FontName = existing.FontName,
                FontSizeRatio = existing.FontSizeRatio
            };
        }
    }

    /// <summary>
    /// 言語色設定をフル更新（フォント含む）
    /// </summary>
    public void SetLanguageColorFull(string languageKey, string colorHex, string displayText, string fontName, double fontSizeRatio)
    {
        if (Settings.LanguageColors.ContainsKey(languageKey))
        {
            Settings.LanguageColors[languageKey] = new LanguageColorSettings
            {
                Color = colorHex,
                DisplayText = displayText,
                FontName = fontName,
                FontSizeRatio = fontSizeRatio
            };
        }
    }

    /// <summary>
    /// 設定を再読み込みする
    /// </summary>
    public void ReloadSettings()
    {
        LoadSettings();
    }
}
