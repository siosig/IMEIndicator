using System.Text.Json.Serialization;

namespace IMEIndicatorClock.Models;

/// <summary>
/// アプリケーション全体の設定
/// </summary>
public class AppSettings
{
    /// <summary>
    /// マウスカーソルインジケーター設定
    /// </summary>
    public MouseCursorIndicatorSettings MouseCursorIndicator { get; set; } = new();

    /// <summary>
    /// IME ON時の背景色（HEX）
    /// </summary>
    public string ImeOnColor { get; set; } = "#EF4444";

    /// <summary>
    /// IME ON時の表示文字
    /// </summary>
    public string ImeOnText { get; set; } = "あ";

    /// <summary>
    /// IME OFF時の背景色（HEX）
    /// </summary>
    public string ImeOffColor { get; set; } = "#3B82F6";

    /// <summary>
    /// IME OFF時の表示文字
    /// </summary>
    public string ImeOffText { get; set; } = "A";

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
/// マウスカーソルインジケーター設定
/// </summary>
public class MouseCursorIndicatorSettings
{
    public bool IsVisible { get; set; } = true;
    public bool HideWhenImeOff { get; set; } = true;
    public double Size { get; set; } = 34;
    public double Opacity { get; set; } = 0.9;
    public double OffsetX { get; set; } = 15;
    public double OffsetY { get; set; } = 15;
}
