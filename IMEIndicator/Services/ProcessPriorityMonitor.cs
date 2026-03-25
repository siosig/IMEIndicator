using System.Diagnostics;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// プロセス優先度の定期監視エンジン。
/// 単一 PeriodicTimer で全ルールを評価し、指数バックオフで間隔を自動調整する。
/// </summary>
public class ProcessPriorityMonitor : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly IProcessPriorityService _service;
    private readonly Dictionary<ProcessPriorityRule, RuleRuntimeState> _runtimeStates = [];
    private readonly object _lock = new();

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
    /// 監視を開始する
    /// </summary>
    public void Start(IReadOnlyList<ProcessPriorityRule> rules)
    {
        Stop();
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

        // バックグラウンドタスクの完了を短時間待機
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

        // 有効なルールがあり、ループが未開始なら開始
        if (!_isRunning && rules.Any(r => r.IsValid && r.IsEnabled))
        {
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

        // プロセス未起動
        if (processes.Count == 0)
        {
            state.ResetAfterSkipOrError(now, MonitorResult.Skipped);
            return;
        }

        // 全インスタンスを評価
        bool anyChanged = false;
        bool anyFailed = false;

        foreach (var (pid, currentPriority) in processes)
        {
            if (currentPriority == targetClass)
                continue; // 既に目標通り

            bool success = _service.SetPriority(pid, targetClass);
            if (success)
                anyChanged = true;
            else
                anyFailed = true;
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
            // 全インスタンスが既に目標通り → バックオフ
            state.BackoffIncrease(now);
        }
    }

    /// <summary>
    /// ルールと RuntimeState の同期（ロック内処理）
    /// </summary>
    private void SyncRuleStates(IReadOnlyList<ProcessPriorityRule> rules)
    {
        lock (_lock)
        {
            // HashSet で O(1) ルックアップ
            var ruleSet = new HashSet<ProcessPriorityRule>(rules);

            var toRemove = _runtimeStates.Keys
                .Where(k => !ruleSet.Contains(k))
                .ToList();
            foreach (var key in toRemove)
                _runtimeStates.Remove(key);

            foreach (var rule in rules)
            {
                if (!_runtimeStates.ContainsKey(rule))
                    _runtimeStates[rule] = new RuleRuntimeState(rule);
            }
        }
    }

    /// <summary>
    /// タイマーループを開始する（二重開始を防止）
    /// </summary>
    private void StartLoop()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TickInterval);
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
