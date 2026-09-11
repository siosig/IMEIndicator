// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="RuleRuntimeState"/> のテスト。現行 tests/cpp/unit/RuleRuntimeStateTests.cpp の移植。
/// </summary>
public sealed class RuleRuntimeStateTests
{
    private static ProcessPriorityRule MakeRule(int maxBackoff = 6) => new()
    {
        ProcessName = "chrome",
        TargetPriority = PriorityLevel.BelowNormal,
        MaxBackoffExponent = maxBackoff,
        IsEnabled = true,
        UseECoreOnly = false,
    };

    [Fact]
    public void InitiallyDueAndExponentZero()
    {
        RuleRuntimeState state = new(MakeRule(), 1);

        Assert.Equal(0, state.CurrentExponent);
        Assert.True(state.IsDue(RuleRuntimeState.Clock.Now));
        Assert.Null(state.LastResult);
    }

    [Fact]
    public void BackoffIncreaseUpToMax()
    {
        RuleRuntimeState state = new(MakeRule(3), 1);
        TimeSpan now = RuleRuntimeState.Clock.Now;

        for (int i = 0; i < 10; i++)
        {
            state.BackoffIncrease(now);
        }

        Assert.Equal(3, state.CurrentExponent);
        Assert.Equal(MonitorResult.Skipped, state.LastResult);
    }

    [Fact]
    public void ResetAfterChangeReturnsToZero()
    {
        RuleRuntimeState state = new(MakeRule(3), 1);
        TimeSpan now = RuleRuntimeState.Clock.Now;

        state.BackoffIncrease(now);
        state.BackoffIncrease(now);
        state.ResetAfterChange(now);

        Assert.Equal(0, state.CurrentExponent);
        Assert.Equal(MonitorResult.Success, state.LastResult);
    }

    [Fact]
    public void ResetAfterFailedKeepsResult()
    {
        RuleRuntimeState state = new(MakeRule(), 1);
        TimeSpan now = RuleRuntimeState.Clock.Now;

        state.ResetAfterSkipOrError(now, MonitorResult.Failed);

        Assert.Equal(0, state.CurrentExponent);
        Assert.Equal(MonitorResult.Failed, state.LastResult);
    }

    [Fact]
    public void IntervalDoublesWithExponent()
    {
        RuleRuntimeState state = new(MakeRule(), 1);
        TimeSpan now = RuleRuntimeState.Clock.Now;

        Assert.Equal(1, state.CurrentIntervalSeconds);
        state.BackoffIncrease(now);
        Assert.Equal(2, state.CurrentIntervalSeconds);
        state.BackoffIncrease(now);
        Assert.Equal(4, state.CurrentIntervalSeconds);
    }

    [Fact]
    public void UpdatePollingIntervalChangesEffectiveInterval()
    {
        RuleRuntimeState state = new(MakeRule(), 1);

        Assert.Equal(1, state.CurrentIntervalSeconds);
        state.UpdatePollingInterval(5);
        Assert.Equal(5, state.CurrentIntervalSeconds);
    }
}
