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
/// <see cref="ProcessPriorityService"/> / <see cref="PriorityLevelMapping"/> のテスト。
/// 現行 tests/cpp/unit/ProcessPriorityServiceTests.cpp の移植 + PriorityClassMapping
/// （T011 で先送りされたテスト。ProcessPriorityRuleTests.cs のコメント参照）。
/// </summary>
public sealed class ProcessPriorityServiceTests
{
    [Fact]
    public void EnumerateDistinctProcessNames_ReturnsNonEmpty()
    {
        // テスト実行中は少なくとも自プロセスが存在するため非空になる
        IReadOnlyList<string> names = ProcessPriorityService.EnumerateDistinctProcessNames();
        Assert.NotEmpty(names);
    }

    [Fact]
    public void EnumerateDistinctProcessNames_NoDuplicates()
    {
        IReadOnlyList<string> names = ProcessPriorityService.EnumerateDistinctProcessNames();
        for (int i = 1; i < names.Count; i++)
        {
            Assert.NotEqual(0, string.Compare(names[i - 1], names[i], StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void EnumerateDistinctProcessNames_AreSorted()
    {
        IReadOnlyList<string> names = ProcessPriorityService.EnumerateDistinctProcessNames();
        for (int i = 1; i < names.Count; i++)
        {
            Assert.True(string.Compare(names[i - 1], names[i], StringComparison.OrdinalIgnoreCase) < 0);
        }
    }

    [Theory]
    [InlineData(PriorityLevel.Idle, ProcessPriorityClass.Idle)]
    [InlineData(PriorityLevel.BelowNormal, ProcessPriorityClass.BelowNormal)]
    [InlineData(PriorityLevel.Normal, ProcessPriorityClass.Normal)]
    [InlineData(PriorityLevel.AboveNormal, ProcessPriorityClass.AboveNormal)]
    [InlineData(PriorityLevel.High, ProcessPriorityClass.High)]
    [InlineData(PriorityLevel.Realtime, ProcessPriorityClass.RealTime)]
    public void PriorityLevelMapping_MapsToExpectedProcessPriorityClass(PriorityLevel level, ProcessPriorityClass expected)
    {
        Assert.Equal(expected, PriorityLevelMapping.ToProcessPriorityClass(level));
    }

    [Fact]
    public void GetProcessPriorities_FindsOwnProcessBySelfName()
    {
        // 自プロセスは常に存在するため、自分自身の名前で問い合わせれば最低 1 件返る。
        string selfName = Process.GetCurrentProcess().ProcessName;
        var service = new ProcessPriorityService();
        IReadOnlyList<ProcessPriorityEntry> entries = service.GetProcessPriorities(selfName);
        Assert.Contains(entries, e => e.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public void IsAccessibleForControl_UnknownProcessName_ReturnsTrue()
    {
        var service = new ProcessPriorityService();
        // 実在しないプロセス名 → 一致 0 件 → 判定不能で true
        Assert.True(service.IsAccessibleForControl("this-process-should-not-exist-xyz123"));
    }

    [Fact]
    public void IsAccessibleForControl_OwnProcess_ReturnsTrue()
    {
        // 自プロセスは自分自身に対して常に PROCESS_SET_INFORMATION を取得できる。
        string selfName = Process.GetCurrentProcess().ProcessName;
        var service = new ProcessPriorityService();
        Assert.True(service.IsAccessibleForControl(selfName));
    }
}
