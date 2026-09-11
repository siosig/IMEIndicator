// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# ポートの単体テスト)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services.Hotkey;

using Xunit;

namespace IMEIndicator.Tests.Hotkey;

/// <summary>
/// InputRouter の単体テスト。
/// 移植元: src/cpp/services/hotkey/InputRouter.h / InputRouter.cpp（1:1 移植の検証）。
/// 各キー種別の代表値（Windows SDK winuser.h の標準仮想キーコード）で ClassifyInput /
/// IsMultimediaKey / IsSpecialKey / IsImeSwitchKey / RequiresLowLevelHook / RouteKeyboardEvent /
/// HandleRawHookMessage を検証する。
/// </summary>
/// <remarks>
/// InputRouter の公開 API は vkey/scanCode/modifiers を <c>uint</c> で受け取るが、xunit の
/// <see cref="Xunit.InlineDataAttribute"/> に渡す定数は C# の属性引数型制約
/// （bool/byte/char/double/float/int/long/sbyte/short/string/enum/object 等に限られ、
/// uint/ulong/ushort は使えない）の対象になるため、テスト側の定数は
/// <see cref="CommandCatalogTests"/> と同じ <c>int</c> で持ち、InputRouter を呼ぶ箇所でのみ
/// <c>(uint)</c> キャストする。
/// </remarks>
public sealed class InputRouterTests
{
    // ---- テスト用の生の仮想キーコード（int。理由はクラス remarks 参照）。
    //      ブラックボックステストのため、意図的に InputRouter 側の private const へは依存せず直接値を持つ。 ----
    private const int VkA = 0x41;             // 通常のキーボードキー（'A'）
    private const int VkVolumeUp = 0xAF;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeMute = 0xAD;
    private const int VkMediaPlayPause = 0xB3;
    private const int VkMediaNextTrack = 0xB0;
    private const int VkMediaPrevTrack = 0xB1;
    private const int VkMediaStop = 0xB2;
    private const int VkLaunchMediaSelect = 0xB5;
    private const int VkLaunchMail = 0xB4;
    private const int VkLaunchApp1 = 0xB6;
    private const int VkLaunchApp2 = 0xB7;
    private const int VkCapital = 0x14;       // CapsLock
    private const int VkNumlock = 0x90;
    private const int VkScroll = 0x91;
    private const int VkSnapshot = 0x2C;      // PrintScreen
    private const int VkPause = 0x13;
    private const int VkKanji = 0x19;
    private const int VkKana = 0x15;          // = VK_HANGUL
    private const int VkDbeDbcschar = 0xF4;   // 半角/全角
    private const int VkDbeSbcschar = 0xF3;   // 全角/半角
    private const int VkProcesskey = 0xE5;
    private const int VkConvert = 0x1C;
    private const int VkNonconvert = 0x1D;
    private const int VkControl = 0x11;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    // 修飾キー・メッセージ定数（InlineData では使わないため uint のままで良い）。
    private const uint ModWin = 0x0008;

    // WM_HOTKEY_RAW_KBD / WM_HOTKEY_RAW_MOUSE は InputRouter.cs 内 internal const（InternalsVisibleTo
    // 未設定のためテストプロジェクトから直接参照できない）。値は WM_APP(0x8000) + 0x100 / + 0x101 で
    // HookEngine.h と同一（InputRouter.cs のコメント参照）。
    private const uint WmHotkeyRawKbd = 0x8100;
    private const uint WmHotkeyRawMouse = 0x8101;

    #region ClassifyInput

    [Theory]
    [InlineData(VkA)]
    [InlineData(0)]
    [InlineData(VkKanji)]
    public void ClassifyInput_BelowVkMouse_ReturnsKeyboard(int vkey)
    {
        Assert.Equal(InputSource.Keyboard, InputRouter.ClassifyInput((uint)vkey, 0));
    }

    [Fact]
    public void ClassifyInput_VkMouse_ReturnsMouse()
    {
        Assert.Equal(InputSource.Mouse, InputRouter.ClassifyInput(InputRouter.VkMouse, 0));
    }

    [Fact]
    public void ClassifyInput_VkDelete_ReturnsSpecial()
    {
        Assert.Equal(InputSource.Special, InputRouter.ClassifyInput(InputRouter.VkDelete, 0));
    }

    [Fact]
    public void ClassifyInput_VkLirc_ReturnsRemote()
    {
        Assert.Equal(InputSource.Remote, InputRouter.ClassifyInput(InputRouter.VkLirc, 0));
    }

    [Fact]
    public void ClassifyInput_VkJoy_ReturnsJoystick()
    {
        Assert.Equal(InputSource.Joystick, InputRouter.ClassifyInput(InputRouter.VkJoy, 0));
    }

    [Fact]
    public void ClassifyInput_IgnoresScanCode()
    {
        // C++ 版と同じく scanCode は判定に使わない（vkey のみで判定する）。
        Assert.Equal(InputRouter.ClassifyInput((uint)VkA, 0), InputRouter.ClassifyInput((uint)VkA, 0x1234));
        Assert.Equal(InputRouter.ClassifyInput(InputRouter.VkMouse, 0), InputRouter.ClassifyInput(InputRouter.VkMouse, 0xFFFFFFFF));
    }

    #endregion

    #region IsMultimediaKey

    [Theory]
    [InlineData(VkVolumeUp)]
    [InlineData(VkVolumeDown)]
    [InlineData(VkVolumeMute)]
    [InlineData(VkMediaPlayPause)]
    [InlineData(VkMediaNextTrack)]
    [InlineData(VkMediaPrevTrack)]
    [InlineData(VkMediaStop)]
    [InlineData(VkLaunchMediaSelect)]
    [InlineData(VkLaunchMail)]
    [InlineData(VkLaunchApp1)]
    [InlineData(VkLaunchApp2)]
    public void IsMultimediaKey_MultimediaKeys_ReturnsTrue(int vkey)
    {
        Assert.True(InputRouter.IsMultimediaKey((uint)vkey));
    }

    [Theory]
    [InlineData(VkA)]
    [InlineData(VkKanji)]
    [InlineData(VkCapital)]
    public void IsMultimediaKey_NonMultimediaKeys_ReturnsFalse(int vkey)
    {
        Assert.False(InputRouter.IsMultimediaKey((uint)vkey));
    }

    #endregion

    #region IsSpecialKey

    [Theory]
    [InlineData(VkCapital)]
    [InlineData(VkNumlock)]
    [InlineData(VkScroll)]
    [InlineData(VkSnapshot)]
    [InlineData(VkPause)]
    public void IsSpecialKey_SpecialKeys_ReturnsTrue(int vkey)
    {
        Assert.True(InputRouter.IsSpecialKey((uint)vkey));
    }

    [Theory]
    [InlineData(VkA)]
    [InlineData(VkVolumeUp)]
    public void IsSpecialKey_NonSpecialKeys_ReturnsFalse(int vkey)
    {
        Assert.False(InputRouter.IsSpecialKey((uint)vkey));
    }

    #endregion

    #region IsImeSwitchKey

    [Theory]
    [InlineData(VkKanji)]
    [InlineData(VkKana)]
    [InlineData(VkDbeDbcschar)]
    [InlineData(VkDbeSbcschar)]
    [InlineData(VkProcesskey)]
    [InlineData(VkConvert)]
    [InlineData(VkNonconvert)]
    public void IsImeSwitchKey_ImeSwitchKeys_ReturnsTrue(int vkey)
    {
        Assert.True(InputRouter.IsImeSwitchKey((uint)vkey));
    }

    [Theory]
    [InlineData(VkA)]
    [InlineData(VkCapital)]
    [InlineData(VkVolumeUp)]
    public void IsImeSwitchKey_NonImeSwitchKeys_ReturnsFalse(int vkey)
    {
        Assert.False(InputRouter.IsImeSwitchKey((uint)vkey));
    }

    #endregion

    #region RequiresLowLevelHook

    [Theory]
    [InlineData(VkKanji)]
    [InlineData(VkKana)]
    [InlineData(VkDbeDbcschar)]
    public void RequiresLowLevelHook_ImeSwitchKey_AlwaysFalse(int vkey)
    {
        // IME 切替キーは Win 修飾キーが付いていても常に false（HotkeyP 機能の対象外）。
        Assert.False(InputRouter.RequiresLowLevelHook((uint)vkey, 0));
        Assert.False(InputRouter.RequiresLowLevelHook((uint)vkey, ModWin));
    }

    [Fact]
    public void RequiresLowLevelHook_WinModifier_ReturnsTrue()
    {
        Assert.True(InputRouter.RequiresLowLevelHook((uint)VkA, ModWin));
    }

    [Fact]
    public void RequiresLowLevelHook_RegularKeyWithoutWinModifier_ReturnsFalse()
    {
        Assert.False(InputRouter.RequiresLowLevelHook((uint)VkA, 0));
    }

    [Theory]
    [InlineData(VkVolumeUp)]
    [InlineData(VkMediaPlayPause)]
    public void RequiresLowLevelHook_MultimediaKey_ReturnsTrue(int vkey)
    {
        Assert.True(InputRouter.RequiresLowLevelHook((uint)vkey, 0));
    }

    [Theory]
    [InlineData(VkCapital)]
    [InlineData(VkSnapshot)]
    public void RequiresLowLevelHook_SpecialKey_ReturnsTrue(int vkey)
    {
        Assert.True(InputRouter.RequiresLowLevelHook((uint)vkey, 0));
    }

    [Fact]
    public void RequiresLowLevelHook_MouseVkeyWithoutModifier_ReturnsFalse()
    {
        // マウス・ジョイスティック・WinLIRC はキーボードフック不要（別スレッド）。
        Assert.False(InputRouter.RequiresLowLevelHook(InputRouter.VkMouse, 0));
        Assert.False(InputRouter.RequiresLowLevelHook(InputRouter.VkJoy, 0));
    }

    [Fact]
    public void RequiresLowLevelHook_MouseVkeyWithWinModifier_ReturnsTrue()
    {
        // 分岐順序の回帰確認: IsImeSwitchKey → MOD_WIN → IsMultimediaKey → IsSpecialKey → vkey>=VkMouse の順で
        // 判定するため、MOD_WIN が立っていれば vkey>=VkMouse の分岐に達する前に true が確定する
        // （C++ 版 requiresLowLevelHook と同じ分岐順序）。
        Assert.True(InputRouter.RequiresLowLevelHook(InputRouter.VkMouse, ModWin));
    }

    #endregion

    #region RouteKeyboardEvent

    [Fact]
    public void RouteKeyboardEvent_NullHandler_DoesNotThrow()
    {
        var exception = Record.Exception(() => InputRouter.RouteKeyboardEvent((uint)VkA, 0, 0, null));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(VkControl)]
    [InlineData(VkLwin)]
    [InlineData(VkRwin)]
    public void RouteKeyboardEvent_ModifierKeyAlone_DoesNotInvokeHandler(int modifierVkey)
    {
        bool invoked = false;
        InputRouter.RouteKeyboardEvent((uint)modifierVkey, 0, 0, (_, _, _) => invoked = true);
        Assert.False(invoked);
    }

    [Fact]
    public void RouteKeyboardEvent_RegularKey_InvokesHandlerWithSameArguments()
    {
        uint capturedVkey = 0, capturedScanCode = 0, capturedModifiers = 0;
        bool invoked = false;

        InputRouter.RouteKeyboardEvent((uint)VkA, 0x1E, ModWin, (vkey, scanCode, modifiers) =>
        {
            invoked = true;
            capturedVkey = vkey;
            capturedScanCode = scanCode;
            capturedModifiers = modifiers;
        });

        Assert.True(invoked);
        Assert.Equal((uint)VkA, capturedVkey);
        Assert.Equal((uint)0x1E, capturedScanCode);
        Assert.Equal(ModWin, capturedModifiers);
    }

    #endregion

    #region HandleRawHookMessage

    [Fact]
    public void HandleRawHookMessage_UnknownMessage_ReturnsFalseAndDoesNotInvokeHandler()
    {
        bool invoked = false;
        bool handled = InputRouter.HandleRawHookMessage(0x0001 /* WM_CREATE 等、対象外のメッセージ */, 0, 0, (_, _, _) => invoked = true);

        Assert.False(handled);
        Assert.False(invoked);
    }

    [Fact]
    public void HandleRawHookMessage_RawMouseMessage_InvokesHandlerWithVkMouse()
    {
        uint capturedVkey = 0, capturedScanCode = 999, capturedModifiers = 0;
        bool invoked = false;

        bool handled = InputRouter.HandleRawHookMessage(WmHotkeyRawMouse, (nint)0x0201 /* WM_LBUTTONDOWN */, 0, (vkey, scanCode, modifiers) =>
        {
            invoked = true;
            capturedVkey = vkey;
            capturedScanCode = scanCode;
            capturedModifiers = modifiers;
        });

        Assert.True(handled);
        Assert.True(invoked);
        Assert.Equal(InputRouter.VkMouse, capturedVkey);
        Assert.Equal((uint)0, capturedScanCode);
        Assert.Equal((uint)0x0201, capturedModifiers);
    }

    [Fact]
    public void HandleRawHookMessage_RawMouseMessage_NullHandler_ReturnsTrueWithoutThrowing()
    {
        var exception = Record.Exception(() => InputRouter.HandleRawHookMessage(WmHotkeyRawMouse, 0, 0, null));
        Assert.Null(exception);
    }

    [Fact]
    public void HandleRawHookMessage_RawKeyboardMessage_ExtractsVkeyAndScanCodeFromParams()
    {
        uint capturedVkey = 0, capturedScanCode = 0;
        bool invoked = false;

        bool handled = InputRouter.HandleRawHookMessage(WmHotkeyRawKbd, (nint)VkA, (nint)0x1E, (vkey, scanCode, _) =>
        {
            invoked = true;
            capturedVkey = vkey;
            capturedScanCode = scanCode;
        });

        Assert.True(handled);
        Assert.True(invoked);
        Assert.Equal((uint)VkA, capturedVkey);
        Assert.Equal((uint)0x1E, capturedScanCode);
    }

    [Fact]
    public void HandleRawHookMessage_RawKeyboardMessage_ModifierKeyAlone_ReturnsTrueButDoesNotInvokeHandler()
    {
        // HandleRawHookMessage 自体は「メッセージを処理した」ことを示す true を返すが、
        // 内部の RouteKeyboardEvent が修飾キー単体を無視するため handler は呼ばれない。
        bool invoked = false;

        bool handled = InputRouter.HandleRawHookMessage(WmHotkeyRawKbd, (nint)VkControl, 0, (_, _, _) => invoked = true);

        Assert.True(handled);
        Assert.False(invoked);
    }

    #endregion
}
