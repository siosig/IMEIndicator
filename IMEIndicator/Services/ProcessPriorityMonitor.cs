using System.Diagnostics;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// プロセス優先度の定期監視エンジン。
/// システム全体で1つのポーリング間隔で全ルールを評価し、指数バックオフで間隔を自動調整する。
/// </summary>
public class ProcessPriorityMonitor : IDisposable
{
    private readonly IProcessPriorityService _service;
    private readonly Dictionary<ProcessPriorityRule, RuleRuntimeState> _runtimeStates = [];
    private readonly object _lock = new();

    private int _pollingIntervalSeconds = 1;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _isRunning;

    public ProcessPriorityMonitor(IProcessPriorityService service)
    {
        _service = service;
    }

    /// <summary>
    /// テスト用：現在の RuntimeState 数
    /// </summary>
    public int RuntimeStatesCount
    {
        get { lock (_lock) return _runtimeStates.Count; }
    }

    /// <summary>
    /// 現在のポーリング間隔（秒）
    /// </summary>
    public int PollingIntervalSeconds => _pollingIntervalSeconds;

    /// <summary>
    /// 監視を開始する
    /// </summary>
    public void Start(IReadOnlyList<ProcessPriorityRule> rules, int pollingIntervalSeconds)
    {
        Stop();
        _pollingIntervalSeconds = Math.Clamp(pollingIntervalSeconds, 1, 1800);
        SyncRuleStates(rules);
        StartLoop();
    }

    /// <summary>
    /// 監視を停止する
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _isRunning = false;

        if (_loopTask is { IsCompleted: false })
        {
            try { _loopTask.Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { /* キャンセル例外は無視 */ }
        }

        _loopTask = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// ルール一覧を更新し、RuleRuntimeState を同期する。
    /// ループが未開始で有効ルールがあれば自動的に開始する。
    /// </summary>
    public void UpdateRules(IReadOnlyList<ProcessPriorityRule> rules)
    {
        SyncRuleStates(rules);

        if (!_isRunning && rules.Any(r => r.IsValid && r.IsEnabled))
        {
            StartLoop();
        }
    }

    /// <summary>
    /// ポーリング間隔を変更する（タイマー再起動）
    /// </summary>
    public void UpdatePollingInterval(int pollingIntervalSeconds)
    {
        var newInterval = Math.Clamp(pollingIntervalSeconds, 1, 1800);
        if (newInterval == _pollingIntervalSeconds) return;

        _pollingIntervalSeconds = newInterval;

        // 全ルールのランタイム状態を更新
        lock (_lock)
        {
            foreach (var state in _runtimeStates.Values)
                state.UpdatePollingInterval(_pollingIntervalSeconds);
        }

        // タイマーを再起動して新しい間隔を適用
        if (_isRunning)
        {
            Stop();
            StartLoop();
        }
    }

    /// <summary>
    /// 単一ルールを評価する（テスト公開用）
    /// </summary>
    public void EvaluateRule(ProcessPriorityRule rule, RuleRuntimeState state)
    {
        var now = DateTime.UtcNow;

        if (!rule.IsValid || !rule.IsEnabled)
        {
            state.ResetAfterSkipOrError(now, MonitorResult.Skipped);
            return;
        }

        var processName = rule.NormalizedProcessName;
        var targetClass = ProcessPriorityRule.ToProcessPriorityClass(rule.TargetPriority);

        IReadOnlyList<(int ProcessId, ProcessPriorityClass CurrentPriority)> processes;
        try
        {
            processes = _service.GetProcessPriorities(processName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessPriorityMonitor] GetProcessPriorities error for '{processName}': {ex.Message}");
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
        long eCoreMask = applyAffinity ? ECoreCpuInfo.Instance.ECoreMask : 0;

        foreach (var (pid, currentPriority) in processes)
        {
            if (currentPriority != targetClass)
            {
                bool success = _service.SetPriority(pid, targetClass);
                if (success)
                    anyChanged = true;
                else
                    anyFailed = true;
            }

            if (applyAffinity)
            {
                long? currentAffinity = _service.GetAffinity(pid);
                if (currentAffinity == null)
                {
                    anyFailed = true;
                }
                else if (currentAffinity.Value != eCoreMask)
                {
                    bool affinityResult = _service.SetAffinity(pid, eCoreMask);
                    if (affinityResult)
                        anyChanged = true;
                    else
                        anyFailed = true;
                }
            }
        }

        if (anyFailed)
            state.ResetAfterSkipOrError(now, MonitorResult.Failed);
        else if (anyChanged)
            state.ResetAfterChange(now);
        else
            state.BackoffIncrease(now);
    }

    private void SyncRuleStates(IReadOnlyList<ProcessPriorityRule> rules)
    {
        lock (_lock)
        {
            var ruleSet = new HashSet<ProcessPriorityRule>(rules);

            var toRemove = _runtimeStates.Keys
                .Where(k => !ruleSet.Contains(k))
                .ToList();
            foreach (var key in toRemove)
                _runtimeStates.Remove(key);

            foreach (var rule in rules)
            {
                if (!_runtimeStates.ContainsKey(rule))
                    _runtimeStates[rule] = new RuleRuntimeState(rule, _pollingIntervalSeconds);
            }
        }
    }

    private void StartLoop()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollingIntervalSeconds));
        _isRunning = true;
        _loopTask = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = DateTime.UtcNow;
                List<KeyValuePair<ProcessPriorityRule, RuleRuntimeState>> snapshot;

                lock (_lock)
                {
                    snapshot = [.. _runtimeStates];
                }

                foreach (var (rule, state) in snapshot)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!state.IsDue(now)) continue;

                    try
                    {
                        EvaluateRule(rule, state);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProcessPriorityMonitor] Unexpected error evaluating rule '{rule.ProcessName}': {ex.Message}");
                        state.ResetAfterSkipOrError(now, MonitorResult.Failed);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
