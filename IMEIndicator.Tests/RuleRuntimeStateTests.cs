using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests;

/// <summary>
/// RuleRuntimeState のバックオフロジックテスト
/// </summary>
public class RuleRuntimeStateTests
{
    private static ProcessPriorityRule CreateRule(int interval = 10, int maxExponent = 6)
        => new() { ProcessName = "test", IntervalSeconds = interval, MaxBackoffExponent = maxExponent };

    [Fact]
    public void Initial_ExponentIsZero()
    {
        var state = new RuleRuntimeState(CreateRule());
        Assert.Equal(0, state.CurrentExponent);
        Assert.Null(state.LastResult);
    }

    [Fact]
    public void Initial_IsDueImmediately()
    {
        var state = new RuleRuntimeState(CreateRule());
        Assert.True(state.IsDue(DateTime.UtcNow));
    }

    [Fact]
    public void BackoffIncrease_DoublesInterval()
    {
        var rule = CreateRule(interval: 10, maxExponent: 6);
        var state = new RuleRuntimeState(rule);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now);
        Assert.Equal(1, state.CurrentExponent);
        Assert.Equal(20, state.CurrentIntervalSeconds); // 10 * 2^1
        Assert.Equal(now.AddSeconds(20), state.NextCheckTime);
        Assert.Equal(MonitorResult.Skipped, state.LastResult);
    }

    [Fact]
    public void BackoffIncrease_ExponentialGrowth()
    {
        var rule = CreateRule(interval: 10, maxExponent: 6);
        var state = new RuleRuntimeState(rule);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // exponent: 0→1→2→3
        state.BackoffIncrease(now);
        Assert.Equal(20, state.CurrentIntervalSeconds); // 10*2

        state.BackoffIncrease(now);
        Assert.Equal(40, state.CurrentIntervalSeconds); // 10*4

        state.BackoffIncrease(now);
        Assert.Equal(80, state.CurrentIntervalSeconds); // 10*8
    }

    [Fact]
    public void BackoffIncrease_CapsAtMaxExponent()
    {
        var rule = CreateRule(interval: 10, maxExponent: 2);
        var state = new RuleRuntimeState(rule);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now); // exponent=1, interval=20
        state.BackoffIncrease(now); // exponent=2, interval=40
        state.BackoffIncrease(now); // exponent=2 (capped), interval=40
        Assert.Equal(2, state.CurrentExponent);
        Assert.Equal(40, state.CurrentIntervalSeconds);
    }

    [Fact]
    public void ResetAfterChange_ResetsToInitialInterval()
    {
        var rule = CreateRule(interval: 10, maxExponent: 6);
        var state = new RuleRuntimeState(rule);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // バックオフを進めてからリセット
        state.BackoffIncrease(now);
        state.BackoffIncrease(now);
        Assert.Equal(2, state.CurrentExponent);

        state.ResetAfterChange(now);
        Assert.Equal(0, state.CurrentExponent);
        Assert.Equal(10, state.CurrentIntervalSeconds);
        Assert.Equal(now.AddSeconds(10), state.NextCheckTime);
        Assert.Equal(MonitorResult.Success, state.LastResult);
    }

    [Fact]
    public void ResetAfterSkipOrError_ResetsExponent()
    {
        var rule = CreateRule(interval: 10, maxExponent: 6);
        var state = new RuleRuntimeState(rule);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now);
        state.BackoffIncrease(now);

        state.ResetAfterSkipOrError(now, MonitorResult.Failed);
        Assert.Equal(0, state.CurrentExponent);
        Assert.Equal(MonitorResult.Failed, state.LastResult);
    }

    [Fact]
    public void Reset_MakesImmediatelyDue()
    {
        var rule = CreateRule(interval: 10);
        var state = new RuleRuntimeState(rule);
        var future = DateTime.UtcNow.AddHours(1);

        state.BackoffIncrease(future); // NextCheckTime を遠い未来に設定
        Assert.False(state.IsDue(DateTime.UtcNow));

        state.Reset();
        Assert.True(state.IsDue(DateTime.UtcNow));
    }

    [Fact]
    public void CurrentIntervalSeconds_WithZeroExponent_EqualsBaseInterval()
    {
        var rule = CreateRule(interval: 30);
        var state = new RuleRuntimeState(rule);
        Assert.Equal(30, state.CurrentIntervalSeconds); // 30 * 2^0 = 30
    }
}
