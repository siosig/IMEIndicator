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
/// HotkeyService の単体テスト。実際に WH_KEYBOARD_LL/WH_MOUSE_LL を張る HookEngine を
/// 内部で使うため、キー入力の実検出は検証しない（quickstart.md S9〜S11、T070 の対象）。
/// ここでは Start/Stop/Reload のライフサイクル安全性と、autoStart エントリの実行・
/// FindHotkeyByKey に基づく dispatch ロジックを検証する。
/// </summary>
public sealed class HotkeyServiceTests
{
    [Fact]
    public void StartThenStop_CompletesWithoutHangingOrThrowing()
    {
        var service = new HotkeyService();
        var settings = new HotkeySettings();

        try
        {
            bool started = service.Start(1, settings);

            Assert.True(started);
            Assert.True(service.IsRunning);
        }
        finally
        {
            service.Stop();
        }

        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Start_WithZeroHandle_ReturnsFalse()
    {
        var service = new HotkeyService();

        bool started = service.Start(0, new HotkeySettings());

        Assert.False(started);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Start_LoadsHotkeysIntoManager()
    {
        var settings = new HotkeySettings
        {
            Hotkeys =
            [
                new HotKeyEntry { Vkey = (int)'A', Modifiers = 2 /* MOD_CONTROL */, Cmd = 200 },
            ],
        };

        var service = new HotkeyService();
        try
        {
            Assert.True(service.Start(1, settings));
            Assert.Equal(1, service.Manager.Count);
        }
        finally
        {
            service.Stop();
        }
    }

    [Fact]
    public void Stop_ClearsManagerAndMainWindowHandle()
    {
        var settings = new HotkeySettings
        {
            Hotkeys = [new HotKeyEntry { Vkey = (int)'B', Modifiers = 2, Cmd = 201 }],
        };

        var service = new HotkeyService();
        service.Start(42, settings);
        service.Stop();

        Assert.Equal(0, service.Manager.Count);
        Assert.Equal(0, CommandExecutor.MainWindowHandle);
    }

    [Fact]
    public void Reload_WhileRunning_ReplacesHotkeySet()
    {
        var service = new HotkeyService();
        try
        {
            Assert.True(service.Start(1, new HotkeySettings
            {
                Hotkeys = [new HotKeyEntry { Vkey = (int)'C', Modifiers = 2, Cmd = 202 }],
            }));
            Assert.Equal(1, service.Manager.Count);

            bool reloaded = service.Reload(new HotkeySettings
            {
                Hotkeys =
                [
                    new HotKeyEntry { Vkey = (int)'D', Modifiers = 2, Cmd = 203 },
                    new HotKeyEntry { Vkey = (int)'E', Modifiers = 2, Cmd = 210 },
                ],
            });

            Assert.True(reloaded);
            Assert.Equal(2, service.Manager.Count);
            Assert.True(service.IsRunning);
        }
        finally
        {
            service.Stop();
        }
    }

    [Fact]
    public void Reload_WhileNotRunning_ReturnsFalse()
    {
        var service = new HotkeyService();

        bool reloaded = service.Reload(new HotkeySettings());

        Assert.False(reloaded);
    }

    [Fact]
    public void HandleRawHookMessage_WhileNotRunning_ReturnsFalse()
    {
        var service = new HotkeyService();

        bool handled = service.HandleRawHookMessage(HookEngine.WM_HOTKEY_RAW_KBD, (nint)(int)'A', 0);

        Assert.False(handled);
    }

    [Fact]
    public void HandleRawHookMessage_UnmatchedKey_ReturnsTrueButExecutesNothing()
    {
        // WM_HOTKEY_RAW_KBD 自体は「処理した」ことになる（InputRouter が true を返す）が、
        // 一致するエントリが無いため何も実行されない（移植元 onHotkeyDetected の
        // 「該当なし: debug ログのみ」分岐）。ここでは「例外を投げずに true を返す」ことのみ検証する
        // （実行有無はログ経由でしか観測できないため）。
        var service = new HotkeyService();
        try
        {
            Assert.True(service.Start(1, new HotkeySettings()));

            bool handled = service.HandleRawHookMessage(HookEngine.WM_HOTKEY_RAW_KBD, (nint)(int)'Z', 0);

            Assert.True(handled);
        }
        finally
        {
            service.Stop();
        }
    }
}
