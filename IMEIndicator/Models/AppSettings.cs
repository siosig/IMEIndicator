using System.Text.Json.Serialization;

namespace IMEIndicator.Models;

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
    /// IME ON時の表示文字
    /// </summary>
    public string ImeOnText { get; set; } = "あ";

    /// <summary>
    /// IME OFF時の表示文字
    /// </summary>
    public string ImeOffText { get; set; } = "A";

    /// <summary>
    /// 初回起動フラグ
    /// </summary>
    public bool IsFirstLaunch { get; set; } = true;

    /// <summary>
    /// プロセス優先度制御ルール一覧
    /// </summary>
    public List<ProcessPriorityRule> ProcessPriorityRules { get; set; } = [];

    /// <summary>
    /// プロセス優先度監視のポーリング間隔（秒）。全ルール共通。最小1、最大1800（30分）。
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 1;
}

/// <summary>
/// マウスカーソルインジケーター設定
/// </summary>
public class MouseCursorIndicatorSettings
{
    public bool IsVisible { get; set; } = true;
    public double Size { get; set; } = 34;
    public double Opacity { get; set; } = 0.9;
    public double OffsetX { get; set; } = 15;
    public double OffsetY { get; set; } = 15;
}
