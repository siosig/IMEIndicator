using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// 監視結果
/// </summary>
public enum MonitorResult
{
    /// <summary>優先度変更成功</summary>
    Success,
    /// <summary>プロセス未起動 or 既に目標優先度</summary>
    Skipped,
    /// <summary>権限不足・例外発生</summary>
    Failed
}

/// <summary>
/// ルールごとの指数バックオフ状態を管理するランタイムオブジェクト
/// </summary>
public class RuleRuntimeState
{
    private readonly ProcessPriorityRule _rule;

    public RuleRuntimeState(ProcessPriorityRule rule)
    {
        _rule = rule;
        Reset();
    }

    /// <summary>
    /// 現在のバックオフ指数（0から開始）
    /// </summary>
    public int CurrentExponent { get; private set; }

    /// <summary>
    /// 現在の実際の監視間隔（秒）
    /// </summary>
    public int CurrentIntervalSeconds => _rule.ValidatedIntervalSeconds * (1 << CurrentExponent);

    /// <summary>
    /// 次回チェック予定時刻
    /// </summary>
    public DateTime NextCheckTime { get; private set; }

    /// <summary>
    /// 前回の監視結果
    /// </summary>
    public MonitorResult? LastResult { get; private set; }

    /// <summary>
    /// チェック時刻に達しているか
    /// </summary>
    public bool IsDue(DateTime now) => now >= NextCheckTime;

    /// <summary>
    /// 優先度が目標通りだった場合：バックオフ指数を増加
    /// </summary>
    public void BackoffIncrease(DateTime now)
    {
        LastResult = MonitorResult.Skipped;
        CurrentExponent = Math.Min(CurrentExponent + 1, _rule.ValidatedMaxBackoffExponent);
        NextCheckTime = now.AddSeconds(CurrentIntervalSeconds);
    }

    /// <summary>
    /// 優先度を変更した場合：バックオフをリセット
    /// </summary>
    public void ResetAfterChange(DateTime now)
    {
        LastResult = MonitorResult.Success;
        CurrentExponent = 0;
        NextCheckTime = now.AddSeconds(CurrentIntervalSeconds);
    }

    /// <summary>
    /// プロセス未起動またはエラーの場合：バックオフをリセット
    /// </summary>
    public void ResetAfterSkipOrError(DateTime now, MonitorResult result)
    {
        LastResult = result;
        CurrentExponent = 0;
        NextCheckTime = now.AddSeconds(CurrentIntervalSeconds);
    }

    /// <summary>
    /// 初期状態にリセット（即座にチェック対象にする）
    /// </summary>
    public void Reset()
    {
        CurrentExponent = 0;
        LastResult = null;
        NextCheckTime = DateTime.UtcNow;
    }
}
