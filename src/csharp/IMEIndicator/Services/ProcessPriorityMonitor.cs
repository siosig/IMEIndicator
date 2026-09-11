// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;
using IMEIndicator.Models;
// WinForms（System.Windows.Forms.Timer）と System.Threading.Timer の名前衝突を避けるため
// 明示エイリアスを使う（MouseTracker.cs と同じ手法）。
using SystemTimer = System.Threading.Timer;

namespace IMEIndicator.Services;

/// <summary>
/// プロセス優先度・アフィニティの定期監視エンジン。
/// 移植元: src/cpp/services/ProcessPriorityMonitor.h / .cpp の class ProcessPriorityMonitor。
/// </summary>
/// <remarks>
/// 移植元は専用スレッド + <c>WaitForSingleObject</c>（停止イベント付き）のループだが、
/// plan.md Step 8 の方針どおり <see cref="System.Threading.Timer"/> ベースに簡略化する。
/// これにより、ThreadPool のコールバックが前回の評価が終わる前に多重起動される可能性が
/// 理論上生じるため（C++ 版の専用スレッドには無かったリスク）、<see cref="_tickInProgress"/> による
/// 簡易な再入防止ガードを追加している（他プロセスに悪影響を与えないことが最優先事項のため）。
/// </remarks>
public sealed class ProcessPriorityMonitor : IDisposable
{
    // std::map<ProcessPriorityRule, ...> と同じ「値の等価性」でルールを識別するための比較子。
    // 移植元 ruleLess() と同じフィールド集合（processName, targetPriority, maxBackoffExponent,
    // isEnabled, useECoreOnly）で判定する。ProcessPriorityRule は可変クラスだが、Start/UpdateRules
    // には毎回 AppSettings から取得した新しいリストが渡される運用のため、キー登録後に同一インスタンスの
    // フィールドが変更されることは無い（移植元も同様に、毎回新しい vector を渡す運用）。
    private sealed class RuleValueComparer : IEqualityComparer<ProcessPriorityRule>
    {
        public static readonly RuleValueComparer Instance = new();

        public bool Equals(ProcessPriorityRule? x, ProcessPriorityRule? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null)
            {
                return false;
            }
            return x.ProcessName == y.ProcessName
                && x.TargetPriority == y.TargetPriority
                && x.MaxBackoffExponent == y.MaxBackoffExponent
                && x.IsEnabled == y.IsEnabled
                && x.UseECoreOnly == y.UseECoreOnly;
        }

        public int GetHashCode(ProcessPriorityRule obj) =>
            HashCode.Combine(obj.ProcessName, obj.TargetPriority, obj.MaxBackoffExponent, obj.IsEnabled, obj.UseECoreOnly);
    }

    private readonly IProcessPriorityService _service;
    private readonly object _lock = new();
    private readonly Dictionary<ProcessPriorityRule, RuleRuntimeState> _runtimeStates = new(RuleValueComparer.Instance);
    private int _pollingIntervalSeconds = 1;
    private SystemTimer? _timer;
    private volatile bool _running;
    private int _tickInProgress; // 0 = 空き、1 = 実行中（再入防止）
    private bool _disposed;

    public ProcessPriorityMonitor(IProcessPriorityService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>現在管理中の RuleRuntimeState 数（テスト用）。</summary>
    public int RuntimeStatesCount
    {
        get { lock (_lock) { return _runtimeStates.Count; } }
    }

    public int PollingIntervalSeconds
    {
        get { lock (_lock) { return _pollingIntervalSeconds; } }
    }

    public void Start(IReadOnlyList<ProcessPriorityRule> rules, int pollingIntervalSeconds)
    {
        Stop();

        lock (_lock)
        {
            _pollingIntervalSeconds = Math.Clamp(pollingIntervalSeconds, 1, 1800);
        }
        SyncRuleStates(rules);

        lock (_lock)
        {
            StartTimerUnlocked();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopTimerUnlocked();
        }
    }

    public void UpdateRules(IReadOnlyList<ProcessPriorityRule> rules)
    {
        SyncRuleStates(rules);

        bool needStart;
        lock (_lock)
        {
            needStart = !_running && rules.Any(r => r.IsValid() && r.IsEnabled);
        }
        if (needStart)
        {
            lock (_lock)
            {
                StartTimerUnlocked();
            }
        }
    }

    public void UpdatePollingInterval(int pollingIntervalSeconds)
    {
        int newInterval = Math.Clamp(pollingIntervalSeconds, 1, 1800);
        bool restart;
        lock (_lock)
        {
            if (newInterval == _pollingIntervalSeconds)
            {
                return;
            }
            _pollingIntervalSeconds = newInterval;
            foreach (RuleRuntimeState state in _runtimeStates.Values)
            {
                state.UpdatePollingInterval(newInterval);
            }
            restart = _running;
            if (restart)
            {
                StopTimerUnlocked();
            }
        }
        if (restart)
        {
            lock (_lock)
            {
                StartTimerUnlocked();
            }
        }
    }

    /// <summary>単一ルールの評価（テスト公開用）。RuleRuntimeState は呼び出し側で生成・所有する。</summary>
    public void EvaluateRule(ProcessPriorityRule rule, RuleRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(state);

        TimeSpan now = RuleRuntimeState.Clock.Now;

        if (!rule.IsValid() || !rule.IsEnabled)
        {
            state.ResetAfterSkipOrError(now, MonitorResult.Skipped);
            return;
        }

        string processName = rule.NormalizedProcessName();
        ProcessPriorityClass targetClass = PriorityLevelMapping.ToProcessPriorityClass(rule.TargetPriority);

        IReadOnlyList<ProcessPriorityEntry> processes;
        try
        {
            processes = _service.GetProcessPriorities(processName);
        }
        catch
        {
            state.ResetAfterSkipOrError(now, MonitorResult.Failed);
            return;
        }

        if (processes.Count == 0)
        {
            state.ResetAfterSkipOrError(now, MonitorResult.Skipped);
            return;
        }

        bool anyChanged = false;
        bool anyFailed = false;
        bool applyAffinity = rule.UseECoreOnly && ECoreCpuInfo.Instance.HasECores;
        nint eCoreMask = applyAffinity ? ECoreCpuInfo.Instance.ECoreMask : 0;

        foreach (ProcessPriorityEntry entry in processes)
        {
            if (entry.CurrentPriorityClass != targetClass)
            {
                if (_service.SetPriority(entry.ProcessId, targetClass))
                {
                    anyChanged = true;
                }
                else
                {
                    anyFailed = true;
                }
            }

            if (applyAffinity)
            {
                nint? current = _service.GetAffinity(entry.ProcessId);
                if (current is null)
                {
                    anyFailed = true;
                }
                else if (current.Value != eCoreMask)
                {
                    if (_service.SetAffinity(entry.ProcessId, eCoreMask))
                    {
                        anyChanged = true;
                    }
                    else
                    {
                        anyFailed = true;
                    }
                }
            }
        }

        if (anyFailed)
        {
            state.ResetAfterSkipOrError(now, MonitorResult.Failed);
        }
        else if (anyChanged)
        {
            state.ResetAfterChange(now);
        }
        else
        {
            state.BackoffIncrease(now);
        }
    }

    private void SyncRuleStates(IReadOnlyList<ProcessPriorityRule> rules)
    {
        lock (_lock)
        {
            // 既存にないルールは削除
            List<ProcessPriorityRule> toRemove = [.. _runtimeStates.Keys.Where(existing => !rules.Contains(existing, RuleValueComparer.Instance))];
            foreach (ProcessPriorityRule key in toRemove)
            {
                _runtimeStates.Remove(key);
            }

            // 新規ルールは追加
            foreach (ProcessPriorityRule rule in rules)
            {
                if (!_runtimeStates.ContainsKey(rule))
                {
                    _runtimeStates[rule] = new RuleRuntimeState(rule, _pollingIntervalSeconds);
                }
            }
        }
    }

    private void StartTimerUnlocked()
    {
        if (_running)
        {
            return;
        }
        _running = true;
        TimeSpan period = TimeSpan.FromSeconds(_pollingIntervalSeconds);
        _timer = new SystemTimer(OnTimerTick, null, period, period);
    }

    private void StopTimerUnlocked()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimerTick(object? state)
    {
        if (!_running)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref _tickInProgress, 1, 0) != 0)
        {
            return; // 前回の評価が終わっていなければこの周期はスキップ（多重実行防止）
        }

        try
        {
            List<KeyValuePair<ProcessPriorityRule, RuleRuntimeState>> snapshot;
            lock (_lock)
            {
                snapshot = [.. _runtimeStates];
            }

            TimeSpan now = RuleRuntimeState.Clock.Now;
            foreach ((ProcessPriorityRule rule, RuleRuntimeState ruleState) in snapshot)
            {
                if (!_running)
                {
                    break;
                }
                if (!ruleState.IsDue(now))
                {
                    continue;
                }
                try
                {
                    EvaluateRule(rule, ruleState);
                }
                catch
                {
                    ruleState.ResetAfterSkipOrError(now, MonitorResult.Failed);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _tickInProgress, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
    }
}
