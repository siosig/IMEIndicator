using System.Text.Json.Serialization;
using System.Windows.Media;

namespace IMEIndicatorClock.Models;

/// <summary>
/// アプリケーション全体の設定
/// </summary>
public class AppSettings
{
    public IMEIndicatorSettings IMEIndicator { get; set; } = new();
    public ClockSettings Clock { get; set; } = new();
    public MouseCursorIndicatorSettings MouseCursorIndicator { get; set; } = new();

    /// <summary>
    /// UI言語設定（空の場合はシステム言語）
    /// </summary>
    public string Language { get; set; } = "";

    /// <summary>
    /// 初回起動フラグ
    /// </summary>
    public bool IsFirstLaunch { get; set; } = true;

    /// <summary>
    /// デバッグ設定
    /// </summary>
    public DebugSettings Debug { get; set; } = new();
}

/// <summary>
/// デバッグ設定
/// </summary>
public class DebugSettings
{
    /// <summary>
    /// IMEモニターのポーリング間隔（ミリ秒）
    /// </summary>
    public int PollingInterval { get; set; } = 100;

    /// <summary>
    /// ログレベル（0: 無効、1-99: コンソールのみ、負: ファイル出力も）
    /// </summary>
    public int LogLevel { get; set; } = 0;
}

/// <summary>
/// IMEインジケーター設定
/// </summary>
public class IMEIndicatorSettings
{
    public bool IsVisible { get; set; } = true;
    public double PositionX { get; set; } = -1;  // -1 = 初回起動時に画面左上に自動配置
    public double PositionY { get; set; } = -1;
    public double Size { get; set; } = 48;
    public double Opacity { get; set; } = 0.7;
    public double FontSizeRatio { get; set; } = 0.5;
    public string FontName { get; set; } = "Segoe UI";
    public int DisplayIndex { get; set; } = 0; // マルチディスプレイ選択

    /// <summary>
    /// 定期ピクセル検証間隔（ミリ秒）
    /// 0: 無効（ウィンドウ切替時のみ）、2000/5000/10000: 有効
    /// </summary>
    public int PixelVerificationIntervalMs { get; set; } = 5000;

    // 言語別の色設定
    public Dictionary<string, LanguageColorSettings> LanguageColors { get; set; } = new()
    {
        ["English"] = new() { Color = "#3B82F6", DisplayText = "A" },
        ["Japanese"] = new() { Color = "#EF4444", DisplayText = "あ" },
        ["Korean"] = new() { Color = "#8B5CF6", DisplayText = "한" },
        ["ChineseSimplified"] = new() { Color = "#22C55E", DisplayText = "简" },
        ["ChineseTraditional"] = new() { Color = "#15803D", DisplayText = "繁" },
        ["Vietnamese"] = new() { Color = "#67E8F9", DisplayText = "V" },
        ["Thai"] = new() { Color = "#06B6D4", DisplayText = "ไ" },
        ["Hindi"] = new() { Color = "#EA580C", DisplayText = "अ" },
        ["Bengali"] = new() { Color = "#F59E0B", DisplayText = "ব" },
        ["Tamil"] = new() { Color = "#10B981", DisplayText = "த" },
        ["Telugu"] = new() { Color = "#14B8A6", DisplayText = "త" },
        ["Nepali"] = new() { Color = "#F472B6", DisplayText = "ने" },
        ["Sinhala"] = new() { Color = "#A78BFA", DisplayText = "සි" },
        ["Myanmar"] = new() { Color = "#FB923C", DisplayText = "မ" },
        ["Khmer"] = new() { Color = "#4ADE80", DisplayText = "ក" },
        ["Lao"] = new() { Color = "#2DD4BF", DisplayText = "ລ" },
        ["Mongolian"] = new() { Color = "#60A5FA", DisplayText = "М" },
        ["Arabic"] = new() { Color = "#F97316", DisplayText = "ع" },
        ["Persian"] = new() { Color = "#C084FC", DisplayText = "ف" },
        ["Hebrew"] = new() { Color = "#EAB308", DisplayText = "ע" },
        ["Ukrainian"] = new() { Color = "#FBBF24", DisplayText = "У" },
        ["Russian"] = new() { Color = "#DB2777", DisplayText = "Я" },
        ["Greek"] = new() { Color = "#2563EB", DisplayText = "Ω" },
        ["Other"] = new() { Color = "#6B7280", DisplayText = "?" }
    };
}

/// <summary>
/// 言語別の色とテキスト設定
/// </summary>
public class LanguageColorSettings
{
    public string Color { get; set; } = "#3B82F6";
    public string DisplayText { get; set; } = "?";
    public string FontName { get; set; } = ""; // 空の場合はグローバル設定を使用
    public double FontSizeRatio { get; set; } = 0; // 0の場合はグローバル設定を使用(20-80%)
}

/// <summary>
/// 時計設定
/// </summary>
public class ClockSettings
{
    public bool IsVisible { get; set; } = true;   // 初回から表示
    public ClockStyle Style { get; set; } = ClockStyle.Analog;
    public double PositionX { get; set; } = -1;   // -1 = 初回起動時に画面右上に自動配置
    public double PositionY { get; set; } = -1;
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 100;
    public double Opacity { get; set; } = 0.7;
    public int DisplayIndex { get; set; } = 0; // マルチディスプレイ選択

    // フォント設定
    public string FontName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 32;
    public string TextColor { get; set; } = "#FFFFFF";

    // 背景色（IME ON/OFF別）
    public bool UseIMEIndicatorColors { get; set; } = true;  // 言語別色をデフォルトで使用
    public string BackgroundColorIMEOn { get; set; } = "#EF4444";
    public string BackgroundColorIMEOff { get; set; } = "#3B82F6";

    // アナログ時計設定
    public double AnalogClockSize { get; set; } = 100;         // アナログ時計サイズ(100-500)
    public string AnalogClockColor { get; set; } = "#FFFF00";  // 黄色
    public string AnalogTextColor { get; set; } = "#FFFFFF";   // 白

    // 表示設定
    public bool ShowSeconds { get; set; } = false;  // シンプルに秒なし
    public string DateFormat { get; set; } = "yyyy/MM/dd";
    public string TimeFormat { get; set; } = "HH:mm:ss";
    public ClockDisplayMode DisplayMode { get; set; } = ClockDisplayMode.TimeOnly;
    public DateTimeLayout Layout { get; set; } = DateTimeLayout.VerticalTimeFirst;
    public DateTimeFormatPreset DateFormatPreset { get; set; } = DateTimeFormatPreset.Normal;
    public DateTimeFormatPreset TimeFormatPreset { get; set; } = DateTimeFormatPreset.Normal;
    public string CustomDateFormat1 { get; set; } = "M/d";
    public string CustomDateFormat2 { get; set; } = "MM-dd";
    public string CustomTimeFormat1 { get; set; } = "H:mm";
    public string CustomTimeFormat2 { get; set; } = "HH:mm:ss";
}

/// <summary>
/// マウスカーソルインジケーター設定
/// </summary>
public class MouseCursorIndicatorSettings
{
    public bool IsVisible { get; set; } = true;   // 初回から表示（これが肝）
    public double Size { get; set; } = 34;
    public double Opacity { get; set; } = 0.9;
    public double OffsetX { get; set; } = 15;
    public double OffsetY { get; set; } = 15;
}

/// <summary>
/// 時計スタイル
/// </summary>
public enum ClockStyle
{
    Digital,
    Analog
}

/// <summary>
/// 時計表示モード
/// </summary>
public enum ClockDisplayMode
{
    TimeOnly,
    DateOnly,
    DateAndTime,
    TimeAndDate
}

/// <summary>
/// 位置プリセット
/// </summary>
public enum PositionPreset
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// 日付/時刻のレイアウト
/// </summary>
public enum DateTimeLayout
{
    VerticalDateFirst,    // 縦(日付、時刻)
    VerticalTimeFirst,    // 縦(時刻、日付)
    HorizontalDateFirst,  // 横(日付、時刻)
    HorizontalTimeFirst,  // 横(時刻、日付)
    DateOnly,             // 日付のみ
    TimeOnly              // 時刻のみ
}

/// <summary>
/// 日付/時刻フォーマットプリセット
/// </summary>
public enum DateTimeFormatPreset
{
    Full,      // フル (2026年1月13日 月曜日 / 14時30分45秒)
    Normal,    // 普通 (2026/01/13 / 14:30:45)
    Short,     // 短い (1/13 / 14:30)
    Custom1,   // カスタム1
    Custom2    // カスタム2
}
