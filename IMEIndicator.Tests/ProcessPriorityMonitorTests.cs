using System.Diagnostics;
using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;
using ECoreCpuInfo = IMEIndicator.Services.ECoreCpuInfo;

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
    public void EvaluateRule_ProcessNotRunning_Skips()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "nonexistent",
            TargetPriority = PriorityLevel.Idle
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
        Assert.Equal(0, state.CurrentExponent);
    }

    [Fact]
    public void EvaluateRule_PriorityNeedsChange_SetsPriority()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Normal);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Success, state.LastResult);
        Assert.Equal(0, state.CurrentExponent);
        Assert.True(_mockService.SetPriorityCalled);
        Assert.Equal(ProcessPriorityClass.Idle, _mockService.LastSetPriority);
    }

    [Fact]
    public void EvaluateRule_PriorityAlreadyTarget_BacksOff()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle,
            MaxBackoffExponent = 6
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Idle);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
        Assert.Equal(1, state.CurrentExponent);
        Assert.False(_mockService.SetPriorityCalled);
    }

    [Fact]
    public void EvaluateRule_MultipleInstances_SetsAllPriorities()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.BelowNormal
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1001, ProcessPriorityClass.Normal);
        _mockService.AddProcess("testproc", 1002, ProcessPriorityClass.Normal);

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(2, _mockService.SetPriorityCallCount);
    }

    [Fact]
    public void EvaluateRule_SetPriorityFails_ResultIsFailed()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Realtime
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Normal);
        _mockService.FailOnSetPriority = true;

        _monitor.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Failed, state.LastResult);
        Assert.Equal(0, state.CurrentExponent);
    }

    [Fact]
    public void UpdateRules_SyncsRuntimeStates()
    {
        var rules = new List<ProcessPriorityRule>
        {
            new() { ProcessName = "proc1" },
            new() { ProcessName = "proc2" }
        };

        _monitor.UpdateRules(rules);
        Assert.Equal(2, _monitor.RuntimeStatesCount);
    }

    [Fact]
    public void UpdateRules_RemovesDeletedRules()
    {
        var rules = new List<ProcessPriorityRule>
        {
            new() { ProcessName = "proc1" },
            new() { ProcessName = "proc2" }
        };

        _monitor.UpdateRules(rules);
        Assert.Equal(2, _monitor.RuntimeStatesCount);

        _monitor.UpdateRules([rules[0]]);
        Assert.Equal(1, _monitor.RuntimeStatesCount);
    }

    [Fact]
    public void PollingIntervalSeconds_DefaultIsOne()
    {
        Assert.Equal(1, _monitor.PollingIntervalSeconds);
    }

    [Fact]
    public void UpdatePollingInterval_ClampsToValidRange()
    {
        _monitor.UpdatePollingInterval(0);
        Assert.Equal(1, _monitor.PollingIntervalSeconds);

        _monitor.UpdatePollingInterval(2000);
        Assert.Equal(1800, _monitor.PollingIntervalSeconds);

        _monitor.UpdatePollingInterval(30);
        Assert.Equal(30, _monitor.PollingIntervalSeconds);
    }

    [Fact]
    public void EvaluateRule_UseECoreOnly_AffinityDiffers_CallsSetAffinity()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle,
            UseECoreOnly = true
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Normal);
        // 現在のアフィニティがE-Coreマスクと異なる値を設定
        _mockService.CurrentAffinity = unchecked((long)0xFFFF);

        _monitor.EvaluateRule(rule, state);

        // E-CoreがあればSetAffinityが呼ばれる、なくても例外にならない
        if (ECoreCpuInfo.Instance.HasECores)
        {
            Assert.True(_mockService.SetAffinityCalled);
            Assert.Equal(ECoreCpuInfo.Instance.ECoreMask, _mockService.LastAffinityMask);
        }
    }

    [Fact]
    public void EvaluateRule_UseECoreOnly_AffinityAlreadyMatches_SkipsSetAffinity()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle,
            UseECoreOnly = true
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Idle);

        // E-Coreがある場合のみテスト可能
        if (ECoreCpuInfo.Instance.HasECores)
        {
            // 現在のアフィニティをE-Coreマスクと一致させる
            _mockService.CurrentAffinity = ECoreCpuInfo.Instance.ECoreMask;

            _monitor.EvaluateRule(rule, state);

            // 優先度もアフィニティも一致 → SetAffinityは呼ばれない
            Assert.False(_mockService.SetAffinityCalled);
            // バックオフが効く
            Assert.Equal(1, state.CurrentExponent);
        }
    }

    [Fact]
    public void EvaluateRule_UseECoreOnlyFalse_DoesNotCallSetAffinity()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = "testproc",
            TargetPriority = PriorityLevel.Idle,
            UseECoreOnly = false
        };
        var state = new RuleRuntimeState(rule, pollingIntervalSeconds: 1);

        _mockService.AddProcess("testproc", 1234, ProcessPriorityClass.Normal);

        _monitor.EvaluateRule(rule, state);

        Assert.False(_mockService.SetAffinityCalled);
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
        public bool SetAffinityCalled { get; private set; }
        public long LastAffinityMask { get; private set; }
        public long? CurrentAffinity { get; set; }

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

        public long? GetAffinity(int processId)
        {
            return CurrentAffinity;
        }

        public bool SetAffinity(int processId, long affinityMask)
        {
            SetAffinityCalled = true;
            LastAffinityMask = affinityMask;
            return true;
        }
    }
}
