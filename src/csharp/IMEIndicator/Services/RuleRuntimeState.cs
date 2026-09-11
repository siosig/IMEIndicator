// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// 監視結果（移植元: src/cpp/services/RuleRuntimeState.h の enum class MonitorResult）。
/// </summary>
public enum MonitorResult
{
    None,
    Success,
    Skipped,
    Failed,
}

/// <summary>
/// ルールごとの指数バックオフ状態。ProcessPriorityMonitor が保持する。
/// 移植元: src/cpp/services/RuleRuntimeState.h / .cpp の class RuleRuntimeState。
/// </summary>
public sealed class RuleRuntimeState
{
    private readonly ProcessPriorityRule _rule;
    private int _pollingIntervalSeconds;
    private int _currentExponent;
    private TimeSpan _nextCheckTime;
    private MonitorResult? _lastResult;

    public RuleRuntimeState(ProcessPriorityRule rule, int pollingIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rule = rule;
        _pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
        Reset();
    }

    /// <summary>
    /// システム時刻の変更（NTP 補正・夏時間等）の影響を受けない単調増加クロック。
    /// 移植元の <c>std::chrono::steady_clock</c>（<c>using clock = std::chrono::steady_clock;</c>）に
    /// 対応する。<see cref="Environment.TickCount64"/>（システム起動からの経過ミリ秒、単調増加）を
    /// 基準の time_point として使う。
    /// </summary>
    public static class Clock
    {
        public static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    /// <summary>現在のバックオフ指数。</summary>
    public int CurrentExponent => _currentExponent;

    /// <summary>現在の実効ポーリング間隔（秒）= pollingIntervalSeconds × 2^CurrentExponent。</summary>
    public int CurrentIntervalSeconds => _pollingIntervalSeconds * (1 << _currentExponent);

    /// <summary>次回チェック予定時刻。</summary>
    public TimeSpan NextCheckTime => _nextCheckTime;

    /// <summary>直近の監視結果。まだ一度も評価していない場合は null（移植元 std::optional の未設定に対応）。</summary>
    public MonitorResult? LastResult => _lastResult;

    /// <summary>指定時刻時点でチェック期限に達しているか。</summary>
    public bool IsDue(TimeSpan now) => now >= _nextCheckTime;

    /// <summary>ポーリング間隔（設定変更）を反映する。1 秒未満は 1 秒にクランプする。</summary>
    public void UpdatePollingInterval(int pollingIntervalSeconds)
    {
        _pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
    }

    /// <summary>優先度が目標と一致 → バックオフ指数を増加させる（上限は rule.maxBackoffExponent、[0,10] にクランプ）。</summary>
    public void BackoffIncrease(TimeSpan now)
    {
        _lastResult = MonitorResult.Skipped;
        int maxExponent = _rule.ValidatedMaxBackoffExponent();
        _currentExponent = Math.Min(_currentExponent + 1, maxExponent);
        _nextCheckTime = now + TimeSpan.FromSeconds(CurrentIntervalSeconds);
    }

    /// <summary>優先度を変更した → バックオフ指数を 0 にリセットする。</summary>
    public void ResetAfterChange(TimeSpan now)
    {
        _lastResult = MonitorResult.Success;
        _currentExponent = 0;
        _nextCheckTime = now + TimeSpan.FromSeconds(CurrentIntervalSeconds);
    }

    /// <summary>プロセス未起動 or エラー → バックオフ指数を 0 にリセットする。</summary>
    public void ResetAfterSkipOrError(TimeSpan now, MonitorResult result)
    {
        _lastResult = result;
        _currentExponent = 0;
        _nextCheckTime = now + TimeSpan.FromSeconds(CurrentIntervalSeconds);
    }

    /// <summary>即時チェック対象に戻す。</summary>
    public void Reset()
    {
        _currentExponent = 0;
        _lastResult = null;
        _nextCheckTime = Clock.Now;
    }
}
