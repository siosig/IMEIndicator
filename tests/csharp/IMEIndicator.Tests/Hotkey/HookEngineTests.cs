// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models.Hotkey;
using IMEIndicator.Services.Hotkey;

using Xunit;

namespace IMEIndicator.Tests.Hotkey;

/// <summary>
/// HookEngine の単体テスト。実際にグローバルフックを張るコンポーネントのため、キー入力の
/// 実検出（本物の合成入力が必要）はここでは検証しない（quickstart.md S9〜S11 の手動検証、T070 の
/// 対象）。ここでは「専用スレッドの起動・停止がハングも例外も無く完了すること」という
/// ライフサイクルの安全性のみを検証する（<c>ProcessPriorityMonitorTests</c> の開始/停止検証と
/// 同じ方針）。
/// </summary>
public sealed class HookEngineTests
{
    [Fact]
    public void StartThenStop_LowLevelMode_CompletesWithoutHangingOrThrowing()
    {
        var engine = new HookEngine();
        try
        {
            bool started = engine.Start(0, HookMode.LowLevel);

            Assert.True(started);
            Assert.True(engine.IsRunning);
        }
        finally
        {
            engine.Stop();
        }

        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Start_NoneMode_DoesNotInstallHooksButStillTracksRunningState()
    {
        // HookMode.None はどちらのフックも有効化しない（移植元 hookThreadProc の条件分岐）が、
        // 専用スレッド自体は起動しメッセージループへ入る。フック未使用でもスレッドの起動/停止が
        // 安全に完了することを確認する。
        var engine = new HookEngine();
        try
        {
            bool started = engine.Start(0, HookMode.None);

            Assert.True(started);
            Assert.True(engine.IsRunning);
        }
        finally
        {
            engine.Stop();
        }
    }

    [Fact]
    public void Start_WhileAlreadyRunning_ReturnsFalseWithoutStartingSecondThread()
    {
        var engine = new HookEngine();
        try
        {
            Assert.True(engine.Start(0, HookMode.LowLevel));
            Assert.False(engine.Start(0, HookMode.LowLevel));
        }
        finally
        {
            engine.Stop();
        }
    }

    [Fact]
    public void Stop_WithoutStart_IsNoOp()
    {
        var engine = new HookEngine();

        Exception? thrown = Record.Exception(engine.Stop);

        Assert.Null(thrown);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void MultipleStartStopCycles_OnSeparateInstances_AllCompleteCleanly()
    {
        // s_instance は static のため、複数インスタンスを順番に（同時にではなく）
        // Start/Stop しても取り違えが起きないことを確認する（前のインスタンスの Stop で
        // s_instance が確実に null 化されてから次の Start が s_instance を上書きする設計）。
        for (int i = 0; i < 3; i++)
        {
            var engine = new HookEngine();
            Assert.True(engine.Start(0, HookMode.LowLevel));
            engine.Stop();
            Assert.False(engine.IsRunning);
        }
    }
}
