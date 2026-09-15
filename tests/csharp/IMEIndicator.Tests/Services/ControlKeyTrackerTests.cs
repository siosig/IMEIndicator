// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="ControlKeyTracker"/> の単体テスト。
/// specs/018-draggable-background-image/data-model.md §1「ControlKeyTracker」の規則表と、
/// quickstart.md「自動テスト観点」の ControlKeyTracker の項目を網羅する。
/// 純粋な状態機械（Win32 API 呼び出しなし）なので、実行環境に依存せず常に実行できる。
/// 仮想キーコードは実装側（NativeConstants）を参照せず、data-model.md §1 の表の値をこのテスト内に
/// リテラルで直接持つ（定数値そのものの取り違えを、実装との結合で見逃さないようにするため）。
/// </summary>
public sealed class ControlKeyTrackerTests
{
    private const uint VkLControl = 0xA2; // VK_LCONTROL
    private const uint VkRControl = 0xA3; // VK_RCONTROL
    private const uint VkControl = 0x11;  // VK_CONTROL（左右を区別しない汎用コード）
    private const uint VkA = 0x41;        // Ctrl 以外の仮想キー（'A'）

    // ---- 初期状態 ----

    [Fact]
    public void IsDown_Initially_IsFalse()
    {
        var tracker = new ControlKeyTracker();

        Assert.False(tracker.IsDown);
    }

    // ---- 左右・汎用それぞれの押下/解放 ----

    [Theory]
    [InlineData(VkLControl)]
    [InlineData(VkRControl)]
    [InlineData(VkControl)]
    public void OnKey_Down_SetsIsDownAndReturnsTrue(uint vkCode)
    {
        var tracker = new ControlKeyTracker();

        bool changed = tracker.OnKey(vkCode, isKeyUp: false);

        Assert.True(changed);
        Assert.True(tracker.IsDown);
    }

    [Theory]
    [InlineData(VkLControl)]
    [InlineData(VkRControl)]
    [InlineData(VkControl)]
    public void OnKey_UpAfterDown_ClearsIsDownAndReturnsTrue(uint vkCode)
    {
        var tracker = new ControlKeyTracker();
        tracker.OnKey(vkCode, isKeyUp: false);

        bool changed = tracker.OnKey(vkCode, isKeyUp: true);

        Assert.True(changed);
        Assert.False(tracker.IsDown);
    }

    // ---- 複数のキーが絡む遷移 ----

    [Fact]
    public void OnKey_ReleasingOneOfTwoHeldKeys_KeepsIsDownTrueAndReturnsFalse()
    {
        var tracker = new ControlKeyTracker();
        tracker.OnKey(VkLControl, isKeyUp: false);
        tracker.OnKey(VkRControl, isKeyUp: false);

        bool changed = tracker.OnKey(VkLControl, isKeyUp: true);

        Assert.False(changed);
        Assert.True(tracker.IsDown);
    }

    [Fact]
    public void OnKey_ReleasingSecondOfTwoHeldKeys_ClearsIsDownAndReturnsTrue()
    {
        var tracker = new ControlKeyTracker();
        tracker.OnKey(VkLControl, isKeyUp: false);
        tracker.OnKey(VkRControl, isKeyUp: false);
        tracker.OnKey(VkLControl, isKeyUp: true);

        bool changed = tracker.OnKey(VkRControl, isKeyUp: true);

        Assert.True(changed);
        Assert.False(tracker.IsDown);
    }

    // ---- オートリピート（同じキーの押下通知が連続する） ----

    [Fact]
    public void OnKey_AutoRepeatSameKeyDown_SecondCallReturnsFalse()
    {
        var tracker = new ControlKeyTracker();
        bool first = tracker.OnKey(VkLControl, isKeyUp: false);

        bool second = tracker.OnKey(VkLControl, isKeyUp: false);

        Assert.True(first);
        Assert.False(second);
        Assert.True(tracker.IsDown);
    }

    // ---- 解放だけの入力（一度も押されていない状態での isKeyUp: true） ----

    [Theory]
    [InlineData(VkLControl)]
    [InlineData(VkRControl)]
    [InlineData(VkControl)]
    public void OnKey_ReleaseWithoutPriorPress_DoesNotTransition(uint vkCode)
    {
        var tracker = new ControlKeyTracker();

        bool changed = tracker.OnKey(vkCode, isKeyUp: true);

        Assert.False(changed);
        Assert.False(tracker.IsDown);
    }

    // ---- Ctrl 以外の仮想キー ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnKey_NonControlKey_OnFreshTracker_AlwaysReturnsFalseAndLeavesIsDownFalse(bool isKeyUp)
    {
        var tracker = new ControlKeyTracker();

        bool changed = tracker.OnKey(VkA, isKeyUp);

        Assert.False(changed);
        Assert.False(tracker.IsDown);
    }

    [Fact]
    public void OnKey_NonControlKey_WhileControlHeld_DoesNotAffectIsDown()
    {
        var tracker = new ControlKeyTracker();
        tracker.OnKey(VkLControl, isKeyUp: false);

        bool changed = tracker.OnKey(VkA, isKeyUp: false);

        Assert.False(changed);
        Assert.True(tracker.IsDown);
    }

    // ---- Reset ----

    [Fact]
    public void Reset_WhenNotPressed_LeavesIsDownFalse()
    {
        var tracker = new ControlKeyTracker();

        tracker.Reset();

        Assert.False(tracker.IsDown);
    }

    [Fact]
    public void Reset_WhilePressed_ClearsIsDown()
    {
        var tracker = new ControlKeyTracker();
        tracker.OnKey(VkLControl, isKeyUp: false);
        tracker.OnKey(VkControl, isKeyUp: false);

        tracker.Reset();

        Assert.False(tracker.IsDown);
    }

    [Fact]
    public void OnKey_DownAfterReset_ReturnsTrueAgain()
    {
        var tracker = new ControlKeyTracker();
        tracker.OnKey(VkLControl, isKeyUp: false);
        tracker.Reset();

        bool changed = tracker.OnKey(VkLControl, isKeyUp: false);

        Assert.True(changed);
        Assert.True(tracker.IsDown);
    }
}
