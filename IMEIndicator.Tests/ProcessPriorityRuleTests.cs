using System.Diagnostics;
using IMEIndicator.Models;
using Xunit;

namespace IMEIndicator.Tests;

/// <summary>
/// ProcessPriorityRule のバリデーションテスト
/// </summary>
public class ProcessPriorityRuleTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(6, 6)]
    [InlineData(10, 10)]
    [InlineData(15, 10)]
    public void ValidatedMaxBackoffExponent_ClampsTo0Through10(int input, int expected)
    {
        var rule = new ProcessPriorityRule { MaxBackoffExponent = input };
        Assert.Equal(expected, rule.ValidatedMaxBackoffExponent);
    }

    [Theory]
    [InlineData("notepad.exe", "notepad")]
    [InlineData("notepad.EXE", "notepad")]
    [InlineData("notepad", "notepad")]
    [InlineData("my.app.exe", "my.app")]
    public void NormalizedProcessName_RemovesExeExtension(string input, string expected)
    {
        var rule = new ProcessPriorityRule { ProcessName = input };
        Assert.Equal(expected, rule.NormalizedProcessName);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("notepad", true)]
    public void IsValid_ChecksProcessNameNotEmpty(string processName, bool expected)
    {
        var rule = new ProcessPriorityRule { ProcessName = processName };
        Assert.Equal(expected, rule.IsValid);
    }

    [Theory]
    [InlineData(PriorityLevel.Idle, ProcessPriorityClass.Idle)]
    [InlineData(PriorityLevel.BelowNormal, ProcessPriorityClass.BelowNormal)]
    [InlineData(PriorityLevel.Normal, ProcessPriorityClass.Normal)]
    [InlineData(PriorityLevel.AboveNormal, ProcessPriorityClass.AboveNormal)]
    [InlineData(PriorityLevel.High, ProcessPriorityClass.High)]
    [InlineData(PriorityLevel.Realtime, ProcessPriorityClass.RealTime)]
    public void ToProcessPriorityClass_CorrectMapping(PriorityLevel level, ProcessPriorityClass expected)
    {
        Assert.Equal(expected, ProcessPriorityRule.ToProcessPriorityClass(level));
    }
}
