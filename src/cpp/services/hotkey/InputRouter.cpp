/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - InputRouter 実装
*/

#include "InputRouter.h"

// IME 拡張仮想キー定数（Microsoft IME 専用、Windows SDK の WinUser.h には含まれず
// imm.h でも version によっては未定義。直接定数値で定義する）。
// 参考: Microsoft IME Virtual Key Codes
//   https://learn.microsoft.com/windows/win32/api/imm/
#ifndef VK_DBE_SBCSCHAR
#define VK_DBE_SBCSCHAR 0xF3  // 全角/半角
#endif
#ifndef VK_DBE_DBCSCHAR
#define VK_DBE_DBCSCHAR 0xF4  // 半角/全角
#endif

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

InputSource InputRouter::classifyInput(UINT vkey, DWORD /*scanCode*/) noexcept
{
    if (vkey == vkMouse)  return InputSource::Mouse;
    if (vkey == vkDelete) return InputSource::Special;
    if (vkey == vkLirc)   return InputSource::Remote;
    if (vkey == vkJoy)    return InputSource::Joystick;
    return InputSource::Keyboard;
}

bool InputRouter::isMultimediaKey(UINT vkey) noexcept
{
    switch (vkey) {
        case VK_VOLUME_UP:
        case VK_VOLUME_DOWN:
        case VK_VOLUME_MUTE:
        case VK_MEDIA_PLAY_PAUSE:
        case VK_MEDIA_NEXT_TRACK:
        case VK_MEDIA_PREV_TRACK:
        case VK_MEDIA_STOP:
        case VK_LAUNCH_MEDIA_SELECT:
        case VK_LAUNCH_MAIL:
        case VK_LAUNCH_APP1:
        case VK_LAUNCH_APP2:
            return true;
        default:
            return false;
    }
}

bool InputRouter::isSpecialKey(UINT vkey) noexcept
{
    switch (vkey) {
        case VK_CAPITAL:    // CapsLock
        case VK_NUMLOCK:    // NumLock
        case VK_SCROLL:     // ScrollLock
        case VK_SNAPSHOT:   // PrintScreen
        case VK_PAUSE:      // Pause/Break
            return true;
        default:
            return false;
    }
}

bool InputRouter::isImeSwitchKey(UINT vkey) noexcept
{
    // IMEIndicator の既存 KeyboardHook が IME 状態検出のために観察するキー。
    // HotkeyP の HookEngine はこれらをホットキーとして横取りせず、IME 動作を尊重する。
    switch (vkey) {
        case VK_KANJI:           // 0x19  漢字キー
        case VK_KANA:            // 0x15  かな (= VK_HANGUL)
        case VK_DBE_DBCSCHAR:    // 0xF3  全角/半角
        case VK_DBE_SBCSCHAR:    // 0xF4  半角/全角
        case VK_PROCESSKEY:      // 0xE5  IME 処理中
        case VK_CONVERT:         // 0x1C  変換
        case VK_NONCONVERT:      // 0x1D  無変換
            return true;
        default:
            return false;
    }
}

bool InputRouter::requiresLowLevelHook(UINT vkey, UINT modifiers) noexcept
{
    // IME 切替キーは IMEIndicator KeyboardHook の責務 → HookEngine は観察対象外（spec FR-021）
    if (isImeSwitchKey(vkey)) return false;

    // Win キーを含む → システムが先に処理するため LL フック必要
    if (modifiers & MOD_WIN) return true;

    // マルチメディアキーは RegisterHotKey では登録できない → LL フック必要
    if (isMultimediaKey(vkey)) return true;

    // 特殊キーも LL フック必要
    if (isSpecialKey(vkey)) return true;

    // マウス・ジョイスティック・WinLIRC はキーボードフック不要（別スレッド）
    if (vkey >= vkMouse) return false;

    return false;
}

void InputRouter::routeKeyboardEvent(
    UINT vkey, DWORD scanCode,
    UINT currentModifiers,
    const std::function<void(UINT, DWORD, UINT)>& handler) noexcept
{
    if (!handler) return;
    // 修飾キー単体は無視
    switch (vkey) {
    case VK_CONTROL: case VK_LCONTROL: case VK_RCONTROL:
    case VK_SHIFT:   case VK_LSHIFT:   case VK_RSHIFT:
    case VK_MENU:    case VK_LMENU:    case VK_RMENU:
    case VK_LWIN:    case VK_RWIN:
        return;
    default:
        break;
    }
    handler(vkey, scanCode, currentModifiers);
}

// 現在押されている修飾キーを GetAsyncKeyState で取得
static UINT getCurrentModifiers() noexcept
{
    UINT mods = 0;
    if (GetAsyncKeyState(VK_CONTROL) & 0x8000) mods |= MOD_CONTROL;
    if (GetAsyncKeyState(VK_SHIFT)   & 0x8000) mods |= MOD_SHIFT;
    if (GetAsyncKeyState(VK_MENU)    & 0x8000) mods |= MOD_ALT;
    if ((GetAsyncKeyState(VK_LWIN) | GetAsyncKeyState(VK_RWIN)) & 0x8000)
        mods |= MOD_WIN;
    return mods;
}

bool InputRouter::handleRawHookMessage(
    UINT msg, WPARAM wParam, LPARAM lParam,
    const std::function<void(UINT, DWORD, UINT)>& handler) noexcept
{
    if (msg == WM_HOTKEY_RAW_KBD) {
        const UINT  vkey     = static_cast<UINT>(wParam);
        const DWORD scanCode = static_cast<DWORD>(lParam);
        const UINT  mods     = getCurrentModifiers();
        routeKeyboardEvent(vkey, scanCode, mods, handler);
        return true;
    }
    if (msg == WM_HOTKEY_RAW_MOUSE) {
        // マウスイベント: WPARAM = WM_LBUTTONDOWN 等, LPARAM = mouseData
        // vkMouse (512) を仮想キーとして使用
        if (handler) {
            handler(vkMouse, 0, static_cast<UINT>(wParam));
        }
        return true;
    }
    return false;
}

} // namespace imeindicator::services::hotkey
