using System.Diagnostics;
using System.Text.Json.Serialization;

namespace IMEIndicator.Models;

/// <summary>
/// Windowsプロセス優先度（タスクマネージャー準拠の6段階）
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PriorityLevel
{
    /// <summary>低</summary>
    Idle,
    /// <summary>通常以下</summary>
    BelowNormal,
    /// <summary>通常</summary>
    Normal,
    /// <summary>通常以上</summary>
    AboveNormal,
    /// <summary>高</summary>
    High,
    /// <summary>リアルタイム</summary>
    Realtime
}

/// <summary>
/// プロセス優先度制御ルール
/// </summary>
public class ProcessPriorityRule
{
    /// <summary>
    /// 監視対象プロセス名（.exe拡張子あり/なし両対応）
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// 設定する優先度
    /// </summary>
    public PriorityLevel TargetPriority { get; set; } = PriorityLevel.Normal;

    /// <summary>
    /// バックオフ最大指数。最大間隔 = PollingIntervalSeconds * 2^MaxBackoffExponent
    /// </summary>
    public int MaxBackoffExponent { get; set; } = 6;

    /// <summary>
    /// ルールの有効/無効
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// E-Core（効率コア）のみに固定するかどうか
    /// </summary>
    public bool UseECoreOnly { get; set; } = false;

    /// <summary>
    /// バリデーション済みの MaxBackoffExponent を返す（0～10）
    /// </summary>
    [JsonIgnore]
    public int ValidatedMaxBackoffExponent => Math.Clamp(MaxBackoffExponent, 0, 10);

    /// <summary>
    /// .exe 拡張子を除去したプロセス名を返す
    /// </summary>
    [JsonIgnore]
    public string NormalizedProcessName
    {
        get
        {
            var name = ProcessName.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            return name;
        }
    }

    /// <summary>
    /// PriorityLevel を ProcessPriorityClass に変換
    /// </summary>
    public static ProcessPriorityClass ToProcessPriorityClass(PriorityLevel level) => level switch
    {
        PriorityLevel.Idle => ProcessPriorityClass.Idle,
        PriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
        PriorityLevel.Normal => ProcessPriorityClass.Normal,
        PriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
        PriorityLevel.High => ProcessPriorityClass.High,
        PriorityLevel.Realtime => ProcessPriorityClass.RealTime,
        _ => ProcessPriorityClass.Normal
    };

    /// <summary>
    /// ルールが有効かどうか（ProcessName が空でない且つ IsEnabled）
    /// </summary>
    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(ProcessName);
}
