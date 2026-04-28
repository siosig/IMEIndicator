#pragma once
/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - InputRouter
 vkey 値に応じた入力ソース判定・フック要否判定
*/

#include <windows.h>
#include <functional>
#include "../../models/hotkey/HotKeyEntry.h"
#include "HookEngine.h"


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// 入力ソースの種別
enum class InputSource {
    Keyboard,   // vkey < 512
    Mouse,      // vkey == vkMouse (512)
    Special,    // vkey == vkDelete (513)
    Remote,     // vkey == vkLirc (514)
    Joystick,   // vkey == vkJoy (515)
};

// 入力ルーティング ユーティリティ（静的関数のみ）
class InputRouter {
public:
    InputRouter() = delete;

    // vkey と scanCode から入力ソースを判定
    [[nodiscard]] static InputSource classifyInput(UINT vkey, DWORD scanCode) noexcept;

    // マルチメディアキー（音量・メディア再生コントロール）かどうか
    [[nodiscard]] static bool isMultimediaKey(UINT vkey) noexcept;

    // 特殊キー（CapsLock, NumLock など）かどうか
    [[nodiscard]] static bool isSpecialKey(UINT vkey) noexcept;

    // IME 切替キー（VK_KANJI / VK_DBE_DBCSCHAR / VK_DBE_SBCSCHAR / VK_KANA / VK_PROCESSKEY 等）かどうか。
    // これらは IMEIndicator の既存 KeyboardHook が観察するキーで、HookEngine はホットキーとして
    // 処理しない（spec FR-021 / research.md R-003）。
    [[nodiscard]] static bool isImeSwitchKey(UINT vkey) noexcept;

    // WH_KEYBOARD_LL が必要かどうか（RegisterHotKey では処理できないキー）。
    // IME 切替キーは常に false を返す（HotkeyP 機能の対象外）。
    [[nodiscard]] static bool requiresLowLevelHook(UINT vkey, UINT modifiers) noexcept;

    // LL フックからのキーボードイベントをルーティング
    // vkey と scanCode を受け取り、登録済みホットキーと照合して実行
    // HotkeyManager への参照を受け取る（疎結合）
    static void routeKeyboardEvent(
        UINT vkey, DWORD scanCode,
        UINT currentModifiers,
        const std::function<void(UINT vkey, DWORD scanCode, UINT modifiers)>& handler) noexcept;

    // WM_HOTKEY_RAW_KBD / WM_HOTKEY_RAW_MOUSE メッセージを処理してハンドラに転送
    // メインウィンドウの WndProc から呼ぶ。
    // 処理した場合は true を返す（WndProc で DefWindowProc をスキップするため）
    [[nodiscard]] static bool handleRawHookMessage(
        UINT msg, WPARAM wParam, LPARAM lParam,
        const std::function<void(UINT vkey, DWORD scanCode, UINT modifiers)>& handler) noexcept;
};

} // namespace imeindicator::services::hotkey
