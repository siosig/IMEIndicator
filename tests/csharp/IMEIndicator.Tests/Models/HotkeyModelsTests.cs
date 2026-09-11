// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models.Hotkey;

using Xunit;

namespace IMEIndicator.Tests.Models;

/// <summary>
/// Hotkey モデル群（HotKeyEntry / HotkeyCategory / HotkeyGlobalOptions / HotkeySettings /
/// MacroDefinition）の単体テスト。
///
/// 対応する C++ 側の専用単体テストファイルは存在しない（tests/cpp 配下に
/// HotKeyEntryTests.cpp 等はなし）ため、specs/010-hotkeyp-merge/contracts/hotkey-entry-schema.md
/// と本タスク（T012）の指示に明記された既定値をそのまま検証する
/// （ProcessPriorityRuleTests.cs / T011 と同じ方針）。
///
/// 以下は本タスクの対象外のためここには含めない:
/// - JSON 文字列変換（HookMode ⇔ "auto" 等）の往復テスト（T014 の HookModeConverter で追加）
/// - Hotkeys/Categories の上限切り捨て（256 件 / 32 件。SettingsManager / AppSettings.Clamp
///   側で扱うため T012 の HotkeySettings 自体は上限を保持しない）
/// - バリデーション・自動カテゴリ判定ロジック（C++ 版 HotKeyEntry::validate() /
///   isCommand() / displayName() / autoCategory() 相当。T012 はデータ型の定義のみ）
/// - MacroDefinition.Inputs（要素型 INPUT が internal のため本テストアセンブリからは不可視）
/// </summary>
public sealed class HotkeyModelsTests
{
    [Fact]
    public void HotKeyEntryDefaultValuesMatchSchema()
    {
        var e = new HotKeyEntry();

        Assert.Equal(string.Empty, e.Note);
        Assert.Equal(0, e.Icon);
        Assert.Equal(0, e.Category);
        Assert.Equal(string.Empty, e.Exe);
        Assert.Equal(string.Empty, e.Args);
        Assert.Equal(string.Empty, e.Dir);
        Assert.Equal(string.Empty, e.Sound);
        Assert.Equal(0, e.Modifiers);
        Assert.Equal(0, e.Vkey);
        Assert.Equal(0, e.ScanCode);
        Assert.Equal(-1, e.Cmd);
        Assert.Equal(0, e.CmdShow);
        Assert.Equal(0, e.Opacity);
        Assert.Equal(1, e.Priority);
        Assert.False(e.Disable);
        Assert.False(e.MultInst);
        Assert.False(e.TrayMenu);
        Assert.False(e.AutoStart);
        Assert.False(e.Ask);
        Assert.False(e.Delay);
        Assert.False(e.Admin);
        Assert.False(e.DistinguishLR);
    }

    [Fact]
    public void HotkeyCategoryDefaultValuesMatchSchema()
    {
        var c = new HotkeyCategory();

        Assert.Equal(12, c.Id);
        Assert.Equal(string.Empty, c.Name);
        Assert.Equal(0, c.DisplayOrder);
        Assert.Equal(string.Empty, c.ColorLabel);
    }

    [Fact]
    public void HotkeyGlobalOptionsDefaultValuesMatchSchema()
    {
        var o = new HotkeyGlobalOptions();

        Assert.Equal(HookMode.Auto, o.HookMode);
        Assert.False(o.DistinguishLeftRightModifiers);
        Assert.NotNull(o.ForegroundExcludeProcesses);
        Assert.Empty(o.ForegroundExcludeProcesses);
        Assert.Equal(0, o.MouseDelayMs);
        Assert.False(o.PlaySoundOnExecution);
    }

    [Fact]
    public void HotkeySettingsDefaultInstanceHasEmptyNonNullLists()
    {
        var s = new HotkeySettings();

        Assert.NotNull(s.Hotkeys);
        Assert.Empty(s.Hotkeys);
        Assert.NotNull(s.Categories);
        Assert.Empty(s.Categories);
        Assert.NotNull(s.GlobalOptions);
        Assert.Equal(HookMode.Auto, s.GlobalOptions.HookMode);
    }

    [Fact]
    public void MacroDefinitionDefaultValuesMatchHeader()
    {
        var m = new MacroDefinition();

        Assert.Equal(string.Empty, m.Source);
        Assert.Equal(TimeSpan.Zero, m.TotalDuration);
        Assert.False(m.Repeat);
        Assert.Equal(1, m.RepeatCount);
    }
}
