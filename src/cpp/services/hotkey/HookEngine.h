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
 HotkeyP モダン再設計 - HookEngine
 WH_KEYBOARD_LL / WH_MOUSE_LL を DLL なしで EXE 内に実装。
 std::jthread でフックスレッドを管理。WIL wil::unique_hhook で RAII 管理。
 joystick / lirc は対象外。
*/

#include <windows.h>
#include <wil/resource.h>
#include <expected>
#include <functional>
#include <system_error>
#include <stop_token>
#include <thread>


#include "../../models/hotkey/HotKeyEntry.h"
#include "../../models/hotkey/HotkeyGlobalOptions.h"  // HookMode 定義

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// HookMode は models::hotkey::HookMode を再利用（HotkeyGlobalOptions.h 由来、永続化対象）。
// services::hotkey 側で独自 enum を持つと models と衝突するため、明示的な型エイリアスで参照する。
using HookMode = ::imeindicator::models::hotkey::HookMode;

// フック戦略（内部用、永続化されない）
enum class HookStrategy {
    None,
    LowLevel,
    SystemHotKey,
};

// HookEngine エラー
enum class HookError {
    AlreadyRunning,
    SetHookFailed,
    ThreadError,
};

std::error_code make_error_code(HookError e);

// LL フック → メインウィンドウ通知用カスタム WM メッセージ
// WPARAM: vkCode, LPARAM: scanCode
inline constexpr UINT WM_HOTKEY_RAW_KBD   = WM_APP + 0x100;
// WPARAM: WM_LBUTTONDOWN 等, LPARAM: mouseData
inline constexpr UINT WM_HOTKEY_RAW_MOUSE = WM_APP + 0x101;

// ホットキーイベントコールバック型
// vkey, scanCode, modifiers を渡す
using HotkeyCallback = std::function<void(UINT vkey, DWORD scanCode, UINT modifiers)>;

// HookEngine クラス
// WH_KEYBOARD_LL + WH_MOUSE_LL フックを管理する
class HookEngine {
public:
    HookEngine() = default;
    ~HookEngine() { stop(); }

    // コピー禁止・ムーブ禁止（HWND/フックハンドルの管理のため）
    HookEngine(const HookEngine&)            = delete;
    HookEngine& operator=(const HookEngine&) = delete;
    HookEngine(HookEngine&&)                 = delete;
    HookEngine& operator=(HookEngine&&)      = delete;

    // フックを開始する（メッセージループスレッドで呼ぶ）
    [[nodiscard]] std::expected<void, HookError>
    start(HWND targetWnd, HookMode mode, HotkeyCallback callback);

    // フックを停止する（デストラクタから自動呼び出し）
    void stop() noexcept;

    // フックが動作中か確認
    [[nodiscard]] bool isRunning() const noexcept { return m_running; }

    // --- 静的ヘルパー（テスト可能） ---

    // 指定キーがメディアキーか判定
    [[nodiscard]] static bool isMultimediaKey(UINT vkey) noexcept;

    // WH_KEYBOARD_LL が必要か判定（Win+key / メディアキー）
    [[nodiscard]] static bool requiresLowLevelHook(UINT vkey, UINT modifiers) noexcept;

    // フックモードとキー情報からフック戦略を選択
    [[nodiscard]] static HookStrategy
    selectHookStrategy(HookMode mode, UINT vkey, UINT modifiers) noexcept;

private:
    // フックスレッドのエントリポイント
    void hookThreadProc(std::stop_token stopToken, HWND targetWnd, HookMode mode);

    // キーボード LL フックプロシージャ（静的）
    static LRESULT CALLBACK keyboardProc(int nCode, WPARAM wParam, LPARAM lParam);

    // マウス LL フックプロシージャ（静的）
    static LRESULT CALLBACK mouseProc(int nCode, WPARAM wParam, LPARAM lParam);

    std::jthread     m_thread;
    bool             m_running    = false;
    HotkeyCallback   m_callback;
    HWND             m_targetWnd  = nullptr;

    // スレッド間共有（静的: フックプロシージャからアクセスするため）
    static HookEngine* s_instance;
};

} // namespace imeindicator::services::hotkey
