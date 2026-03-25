using System.Diagnostics;
using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests;

/// <summary>
/// ProcessPriorityMonitor の監視サイクルテスト（IProcessPriorityService モック使用）
/// </summary>
public class ProcessPriorityMonitorTests : IDisposable
{
    private readonly MockProcessPriorityService _mockService;
    private readonly ProcessPriorityMonitor _monitor;

    public ProcessPriorityMonitorTests()
    {
        _mockService = new MockProcessPriorityService();
        _monitor = new ProcessPriorityMonitor(_mockService);
    }

    public void Dispose()
    {
        _monitor.Dispose();
    }

    [Fact]
    public async Task EvaluateRule_ProcessNotRunning_Skips()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "nonexistent",
            TargetPriority = PriorityLevel.Idle,
            IntervalSeconds = 10
        };
        var state = new RuleRuntimeState(rule);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
        Assert.Equal(0, state.CurrentExponent); // リセット
    }

    [Fact]
    public async Task EvaluateRule_PriorityNeedsChange_SetsPriority()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle,
            IntervalSeconds = 10
        };
        var state = new RuleRuntimeState(rule);

        // プロセスが通常優先度で実行中
        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Normal);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Success, state.LastResult);
        Assert.Equal(0, state.CurrentExponent); // 変更後はリセット
        Assert.True(_mockService.SetPriorityCalled);
        Assert.Equal(ProcessPriorityClass.Idle, _mockService.LastSetPriority);
    }

    [Fact]
    public async Task EvaluateRule_PriorityAlreadyTarget_BacksOff()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle,
            IntervalSeconds = 10,
            MaxBackoffExponent = 6
        };
        var state = new RuleRuntimeState(rule);

        // プロセスが既に目標優先度
        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Idle);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
        Assert.Equal(1, state.CurrentExponent); // バックオフ増加
        Assert.False(_mockService.SetPriorityCalled);
    }

    [Fact]
    public async Task EvaluateRule_MultipleInstances_SetsAllPriorities()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.BelowNormal,
            IntervalSeconds = 10
        };
        var state = new RuleRuntimeState(rule);

        _mockService.AddProcess("testproc", 1001, ProcessPriorityClass.Normal);
        _mockService.AddProcess("testproc", 1002, ProcessPriorityClass.Normal);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(2, _mockService.SetPriorityCallCount);
    }

    [Fact]
    public async Task EvaluateRule_SetPriorityFails_ResultIsFailed()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Realtime,
            IntervalSeconds = 10
        };
        var state = new RuleRuntimeState(rule);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Normal);
        _mockService.FailOnSetPriority = true;

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Failed, state.LastResult);
        Assert.Equal(0, state.CurrentExponent); // エラー時はリセット
    }

    [Fact]
    public void UpdateRules_SyncsRuntimeStates()
    {
        var rules = new List<ProcessPriorityRule>
        {
            new() { ProcessName = "proc1", IntervalSeconds = 10 },
            new() { ProcessName = "proc2", IntervalSeconds = 20 }
        };

        _monitor.UpdateRules(rules);
        Assert.Equal(2, _monitor.RuntimeStatesCount);
    }

    [Fact]
    public void UpdateRules_RemovesDeletedRules()
    {
        var rules = new List<ProcessPriorityRule>
        {
            new() { ProcessName = "proc1", IntervalSeconds = 10 },
            new() { ProcessName = "proc2", IntervalSeconds = 20 }
        };

        _monitor.UpdateRules(rules);
        Assert.Equal(2, _monitor.RuntimeStatesCount);

        // proc2 を削除
        _monitor.UpdateRules([rules[0]]);
        Assert.Equal(1, _monitor.RuntimeStatesCount);
    }

    /// <summary>
    /// テスト用モックサービス
    /// </summary>
    private class MockProcessPriorityService : IProcessPriorityService
    {
        private readonly Dictionary<string, List<(int ProcessId, ProcessPriorityClass Priority)>> _processes = new();
        public bool SetPriorityCalled { get; private set; }
        public int SetPriorityCallCount { get; private set; }
        public ProcessPriorityClass LastSetPriority { get; private set; }
        public bool FailOnSetPriority { get; set; }

        public void AddProcess(string name, int pid, ProcessPriorityClass priority)
        {
            if (!_processes.ContainsKey(name))
                _processes[name] = [];
            _processes[name].Add((pid, priority));
        }

        public IReadOnlyList<(int ProcessId, ProcessPriorityClass CurrentPriority)> GetProcessPriorities(string processName)
        {
            return _processes.TryGetValue(processName, out var list)
                ? list.AsReadOnly()
                : [];
        }

        public bool SetPriority(int processId, ProcessPriorityClass priority)
        {
            SetPriorityCalled = true;
            SetPriorityCallCount++;
            LastSetPriority = priority;
            return !FailOnSetPriority;
        }
    }
}
