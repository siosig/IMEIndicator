using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests;

/// <summary>
/// RuleRuntimeState のバックオフロジックテスト
/// </summary>
public class RuleRuntimeStateTests
{
    private static ProcessPriorityRule CreateRule(int maxExponent = 6)
        => new() { ProcessName = "test", MaxBackoffExponent = maxExponent };

    [Fact]
    public void Initial_ExponentIsZero()
    {
        var state = new RuleRuntimeState(CreateRule(), pollingIntervalSeconds: 1);
        Assert.Equal(0, state.CurrentExponent);
        Assert.Null(state.LastResult);
    }

    [Fact]
    public void Initial_IsDueImmediately()
    {
        var state = new RuleRuntimeState(CreateRule(), pollingIntervalSeconds: 1);
        Assert.True(state.IsDue(DateTime.UtcNow));
    }

    [Fact]
    public void BackoffIncrease_DoublesInterval()
    {
        var state = new RuleRuntimeState(CreateRule(maxExponent: 6), pollingIntervalSeconds: 1);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now);
        Assert.Equal(1, state.CurrentExponent);
        Assert.Equal(2, state.CurrentIntervalSeconds); // 1 * 2^1
        Assert.Equal(now.AddSeconds(2), state.NextCheckTime);
        Assert.Equal(MonitorResult.Skipped, state.LastResult);
    }

    [Fact]
    public void BackoffIncrease_ExponentialGrowth()
    {
        var state = new RuleRuntimeState(CreateRule(maxExponent: 6), pollingIntervalSeconds: 1);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now); // exponent=1, interval=2
        Assert.Equal(2, state.CurrentIntervalSeconds);

        state.BackoffIncrease(now); // exponent=2, interval=4
        Assert.Equal(4, state.CurrentIntervalSeconds);

        state.BackoffIncrease(now); // exponent=3, interval=8
        Assert.Equal(8, state.CurrentIntervalSeconds);
    }

    [Fact]
    public void BackoffIncrease_CapsAtMaxExponent()
    {
        var state = new RuleRuntimeState(CreateRule(maxExponent: 2), pollingIntervalSeconds: 1);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now); // exponent=1, interval=2
        state.BackoffIncrease(now); // exponent=2, interval=4
        state.BackoffIncrease(now); // exponent=2 (capped), interval=4
        Assert.Equal(2, state.CurrentExponent);
        Assert.Equal(4, state.CurrentIntervalSeconds);
    }

    [Fact]
    public void ResetAfterChange_ResetsToInitialInterval()
    {
        var state = new RuleRuntimeState(CreateRule(maxExponent: 6), pollingIntervalSeconds: 1);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        state.BackoffIncrease(now);
        state.BackoffIncrease(now);
        Assert.Equal(2, state.CurrentExponent);

        state.ResetAfterChange(now);
        Assert.Equal(0, state.CurrentExponent);
        Assert.Equal(1, state.CurrentIntervalSeconds);
        Assert.Equal(now.AddSeconds(1), state.NextCheckTime);
        Assert.Equal(MonitorResult.Success, state.LastResult);
    }

    [Fact]
    public void ResetAfterSkipOrError_ResetsExponent()
    {
        var state = new RuleRuntimeState(CreateRule(maxExponent: 6), pollingIntervalSeconds: 1);
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
        var state = new RuleRuntimeState(CreateRule(), pollingIntervalSeconds: 1);
        var future = DateTime.UtcNow.AddHours(1);

        state.BackoffIncrease(future);
        Assert.False(state.IsDue(DateTime.UtcNow));

        state.Reset();
        Assert.True(state.IsDue(DateTime.UtcNow));
    }

    [Fact]
    public void CurrentIntervalSeconds_UsesPollingInterval()
    {
        var state = new RuleRuntimeState(CreateRule(), pollingIntervalSeconds: 10);
        Assert.Equal(10, state.CurrentIntervalSeconds); // 10 * 2^0 = 10

        var now = DateTime.UtcNow;
        state.BackoffIncrease(now);
        Assert.Equal(20, state.CurrentIntervalSeconds); // 10 * 2^1 = 20
    }

    [Fact]
    public void UpdatePollingInterval_ChangesBaseInterval()
    {
        var state = new RuleRuntimeState(CreateRule(), pollingIntervalSeconds: 1);
        Assert.Equal(1, state.CurrentIntervalSeconds);

        state.UpdatePollingInterval(5);
        Assert.Equal(5, state.CurrentIntervalSeconds); // 5 * 2^0 = 5
    }
}
