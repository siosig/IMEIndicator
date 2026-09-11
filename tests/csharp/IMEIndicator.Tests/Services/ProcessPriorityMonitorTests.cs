// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;
using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="ProcessPriorityMonitor"/> のテスト。現行 tests/cpp/unit/ProcessPriorityMonitorTests.cpp の移植。
/// </summary>
public sealed class ProcessPriorityMonitorTests
{
    // 現行 FakePriorityService（C++）と等価のフェイク実装。
    private sealed class FakePriorityService : IProcessPriorityService
    {
        public Dictionary<string, List<ProcessPriorityEntry>> MockProcesses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int GetCalls;
        public int SetCalls;
        public bool FailSetPriority;

        public IReadOnlyList<ProcessPriorityEntry> GetProcessPriorities(string processNameNoExt)
        {
            Interlocked.Increment(ref GetCalls);
            // 独立したコピーを返す（C++ 版の std::vector 値返しと同じ意味。呼び出し元が列挙している間に
            // SetPriority が内部状態を書き換えても、既に取得済みのスナップショットには影響しない）。
            return MockProcesses.TryGetValue(processNameNoExt, out List<ProcessPriorityEntry>? list) ? [.. list] : [];
        }

        public bool SetPriority(int processId, ProcessPriorityClass priorityClass)
        {
            Interlocked.Increment(ref SetCalls);
            if (FailSetPriority)
            {
                return false;
            }
            foreach (List<ProcessPriorityEntry> list in MockProcesses.Values)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].ProcessId == processId)
                    {
                        list[i] = list[i] with { CurrentPriorityClass = priorityClass };
                    }
                }
            }
            return true;
        }

        public nint? GetAffinity(int processId) => 1;

        public bool SetAffinity(int processId, nint affinityMask) => true;

        public bool IsAccessibleForControl(string processNameNoExt) => true;
    }

    private static ProcessPriorityRule MakeRule(string name, PriorityLevel priority) => new()
    {
        ProcessName = name,
        TargetPriority = priority,
        MaxBackoffExponent = 6,
        IsEnabled = true,
        UseECoreOnly = false,
    };

    [Fact]
    public void EvaluateRule_SkippedWhenNoProcess()
    {
        var svc = new FakePriorityService();
        var mon = new ProcessPriorityMonitor(svc);
        ProcessPriorityRule rule = MakeRule("chrome", PriorityLevel.BelowNormal);
        var state = new RuleRuntimeState(rule, 1);

        mon.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
    }

    [Fact]
    public void EvaluateRule_ChangesPriority()
    {
        var svc = new FakePriorityService();
        svc.MockProcesses["chrome"] = [new ProcessPriorityEntry(1234, ProcessPriorityClass.Normal)];
        var mon = new ProcessPriorityMonitor(svc);
        ProcessPriorityRule rule = MakeRule("chrome", PriorityLevel.BelowNormal);
        var state = new RuleRuntimeState(rule, 1);

        mon.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Success, state.LastResult);
        Assert.True(svc.SetCalls >= 1);
    }

    [Fact]
    public void EvaluateRule_SkippedWhenAlreadyTarget()
    {
        var svc = new FakePriorityService();
        svc.MockProcesses["chrome"] = [new ProcessPriorityEntry(1234, ProcessPriorityClass.BelowNormal)];
        var mon = new ProcessPriorityMonitor(svc);
        ProcessPriorityRule rule = MakeRule("chrome", PriorityLevel.BelowNormal);
        var state = new RuleRuntimeState(rule, 1);

        mon.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
    }

    [Fact]
    public void EvaluateRule_FailedWhenSetFails()
    {
        var svc = new FakePriorityService();
        svc.MockProcesses["chrome"] = [new ProcessPriorityEntry(1234, ProcessPriorityClass.Normal)];
        svc.FailSetPriority = true;
        var mon = new ProcessPriorityMonitor(svc);
        ProcessPriorityRule rule = MakeRule("chrome", PriorityLevel.BelowNormal);
        var state = new RuleRuntimeState(rule, 1);

        mon.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Failed, state.LastResult);
    }

    [Fact]
    public void UpdateRules_SyncsRuntimeStates()
    {
        var svc = new FakePriorityService();
        var mon = new ProcessPriorityMonitor(svc);
        List<ProcessPriorityRule> rules =
        [
            MakeRule("chrome", PriorityLevel.BelowNormal),
            MakeRule("opera", PriorityLevel.Normal),
        ];

        mon.UpdateRules(rules);
        Assert.Equal(2, mon.RuntimeStatesCount);

        rules.RemoveAt(rules.Count - 1);
        mon.UpdateRules(rules);
        Assert.Equal(1, mon.RuntimeStatesCount);

        mon.Stop();
    }

    [Fact]
    public void EvaluateRule_DisabledRule_IsSkipped()
    {
        var svc = new FakePriorityService();
        svc.MockProcesses["chrome"] = [new ProcessPriorityEntry(1234, ProcessPriorityClass.Normal)];
        var mon = new ProcessPriorityMonitor(svc);
        ProcessPriorityRule rule = MakeRule("chrome", PriorityLevel.BelowNormal);
        rule.IsEnabled = false;
        var state = new RuleRuntimeState(rule, 1);

        mon.EvaluateRule(rule, state);

        Assert.Equal(MonitorResult.Skipped, state.LastResult);
        Assert.Equal(0, svc.GetCalls);
    }

    [Fact]
    public void StartAndStop_DoesNotThrow()
    {
        var svc = new FakePriorityService();
        using var mon = new ProcessPriorityMonitor(svc);
        List<ProcessPriorityRule> rules = [MakeRule("chrome", PriorityLevel.BelowNormal)];

        mon.Start(rules, 1);
        Assert.Equal(1, mon.RuntimeStatesCount);
        mon.Stop();
    }
}
