// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;

using Xunit;

namespace IMEIndicator.Tests.Models;

/// <summary>
/// ProcessPriorityRule の単体テスト。
/// 移植元: tests/cpp/unit/ProcessPriorityRuleTests.cpp。
///
/// 移植元には他に PriorityLevelStringRoundTrip（priorityLevelToString /
/// tryParsePriorityLevel）、JsonRoundTrip（to_json / from_json）、
/// PriorityClassMapping（priorityLevelToProcessPriorityClass）の 3 テストがあるが、
/// これらが検証する機能は T011 の対象外（文字列変換コンバータは T014、
/// Win32 優先度クラスへのマッピングは T037 で実装予定）のため、本ファイルには含めない。
/// </summary>
public sealed class ProcessPriorityRuleTests
{
    [Fact]
    public void NormalizedNameStripsExeAndTrim()
    {
        var r = new ProcessPriorityRule();

        r.ProcessName = "  chrome.exe ";
        Assert.Equal("chrome", r.NormalizedProcessName());

        r.ProcessName = "NOTEPAD.EXE";
        Assert.Equal("NOTEPAD", r.NormalizedProcessName());

        r.ProcessName = "foo";
        Assert.Equal("foo", r.NormalizedProcessName());
    }

    [Fact]
    public void IsValidRejectsBlankNames()
    {
        var r = new ProcessPriorityRule();

        r.ProcessName = "";
        Assert.False(r.IsValid());

        r.ProcessName = "   ";
        Assert.False(r.IsValid());

        r.ProcessName = "x";
        Assert.True(r.IsValid());
    }

    [Fact]
    public void ValidatedMaxBackoffExponentClamps()
    {
        var r = new ProcessPriorityRule();

        r.MaxBackoffExponent = -5;
        Assert.Equal(0, r.ValidatedMaxBackoffExponent());

        r.MaxBackoffExponent = 99;
        Assert.Equal(10, r.ValidatedMaxBackoffExponent());

        r.MaxBackoffExponent = 5;
        Assert.Equal(5, r.ValidatedMaxBackoffExponent());
    }

    // 移植元にはない補足テスト。data-model.md §1 と本タスクの指示に明記された既定値を
    // そのまま検証する（C++ 側はメンバ初期化子がコンパイル時に保証するが、C# 側は
    // 実装の取り違えを検出するために明示的にテストする）。
    [Fact]
    public void DefaultValuesMatchSpec()
    {
        var r = new ProcessPriorityRule();

        Assert.Equal(string.Empty, r.ProcessName);
        Assert.Equal(PriorityLevel.Normal, r.TargetPriority);
        Assert.Equal(6, r.MaxBackoffExponent);
        Assert.True(r.IsEnabled);
        Assert.False(r.UseECoreOnly);
    }
}
