using System.Text.Json.Serialization;

namespace IMEIndicatorClock.Models;

/// <summary>
/// UI Automation で発見した IME Indicator 要素の記録
/// </summary>
public class UIADiscoveryLog
{
    /// <summary>
    /// 発見した要素のリスト
    /// </summary>
    public List<UIADiscoveredElement> Elements { get; set; } = new();

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// 発見した UI Automation 要素
/// </summary>
public class UIADiscoveredElement
{
    /// <summary>
    /// 一意識別キー: "{ParentName}|{ControlTypeId}|{Name}"
    /// 例: "IME Indicator|0xC364|あ"
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// 要素の Name プロパティ
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 要素の ClassName
    /// </summary>
    public string ClassName { get; set; } = "";

    /// <summary>
    /// ControlType の ID (例: 0xC364 = UIA_TextControlTypeId)
    /// </summary>
    public int ControlTypeId { get; set; }

    /// <summary>
    /// ControlType のプログラム名 (例: "ControlType.Text")
    /// </summary>
    public string ControlTypeName { get; set; } = "";

    /// <summary>
    /// LocalizedControlType (例: "テキスト", "ボタン")
    /// </summary>
    public string LocalizedControlType { get; set; } = "";

    /// <summary>
    /// 親要素の Name
    /// </summary>
    public string ParentName { get; set; } = "";

    /// <summary>
    /// 親要素の ControlType
    /// </summary>
    public string ParentControlType { get; set; } = "";

    /// <summary>
    /// 初めて発見した日時
    /// </summary>
    public DateTime FirstSeen { get; set; }

    /// <summary>
    /// 最後に確認した日時
    /// </summary>
    public DateTime LastSeen { get; set; }

    /// <summary>
    /// 発見回数
    /// </summary>
    public int SeenCount { get; set; }

    /// <summary>
    /// アクティブウィンドウ名（発見時）
    /// </summary>
    public string ActiveWindowTitle { get; set; } = "";

    /// <summary>
    /// BoundingRectangle (発見時)
    /// </summary>
    public string BoundingRectangle { get; set; } = "";

    /// <summary>
    /// 追加のメモ
    /// </summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// IMEマーク関連かどうか（ButtonControlType で IME/インジケーター を含む）
    /// </summary>
    [JsonIgnore]
    public bool IsIMEMark => ControlTypeId == 0xC350 && // ButtonControlTypeId
                            (Name.Contains("IME") || Name.Contains("インジケーター") || Name.Contains("入力"));

    /// <summary>
    /// IME状態テキストかどうか（TextControlType で 1-2文字）
    /// </summary>
    [JsonIgnore]
    public bool IsIMEStateText => ControlTypeId == 0xC364 && // TextControlTypeId
                                  !string.IsNullOrEmpty(Name) && Name.Length <= 2;
}
