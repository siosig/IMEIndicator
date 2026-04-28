/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - HookEngine 実装
 WH_KEYBOARD_LL / WH_MOUSE_LL フックを DLL なしで EXE 内に実装。
 フックプロシージャは PostMessage のみ実行（< 1ms 保証）。
*/

#include "HookEngine.h"

// --- エラーカテゴリ ---

namespace {

struct HookErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "HookError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<HookError>(ev)) {
        case HookError::AlreadyRunning: return "Hook engine already running";
        case HookError::SetHookFailed:  return "SetWindowsHookEx failed";
        case HookError::ThreadError:    return "Hook thread error";
        }
        return "Unknown HookError";
    }
};

const HookErrorCategory& hookErrorCategory() noexcept {
    static HookErrorCategory cat;
    return cat;
}

} // namespace

// 静的インスタンスポインタ
HookEngine* HookEngine::s_instance = nullptr;

std::error_code make_error_code(HookError e) {
    return {static_cast<int>(e), hookErrorCategory()};
}

// --- 静的ヘルパー ---

bool HookEngine::isMultimediaKey(UINT vkey) noexcept {
    switch (vkey) {
    case VK_VOLUME_MUTE:
    case VK_VOLUME_DOWN:
    case VK_VOLUME_UP:
    case VK_MEDIA_NEXT_TRACK:
    case VK_MEDIA_PREV_TRACK:
    case VK_MEDIA_STOP:
    case VK_MEDIA_PLAY_PAUSE:
    case VK_LAUNCH_MAIL:
    case VK_LAUNCH_MEDIA_SELECT:
    case VK_LAUNCH_APP1:
    case VK_LAUNCH_APP2:
    case VK_BROWSER_BACK:
    case VK_BROWSER_FORWARD:
    case VK_BROWSER_REFRESH:
    case VK_BROWSER_STOP:
    case VK_BROWSER_SEARCH:
    case VK_BROWSER_FAVORITES:
    case VK_BROWSER_HOME:
        return true;
    default:
        return false;
    }
}

bool HookEngine::requiresLowLevelHook(UINT vkey, UINT modifiers) noexcept {
    // Win キーを含む組み合わせ → システムが横取りするため LL 必須
    if (modifiers & MOD_WIN) return true;
    // メディアキー → RegisterHotKey 不可のため LL 必須
    if (isMultimediaKey(vkey)) return true;
    return false;
}

HookStrategy HookEngine::selectHookStrategy(
    HookMode mode, UINT vkey, UINT modifiers) noexcept
{
    switch (mode) {
    case HookMode::None:
        return HookStrategy::None;
    case HookMode::LowLevel:
        return HookStrategy::LowLevel;
    case HookMode::SystemHotKey:
        return HookStrategy::SystemHotKey;
    case HookMode::Auto:
        return requiresLowLevelHook(vkey, modifiers)
            ? HookStrategy::LowLevel
            : HookStrategy::SystemHotKey;
    }
    return HookStrategy::None;
}

// --- フックプロシージャ ---

LRESULT CALLBACK HookEngine::keyboardProc(
    int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode == HC_ACTION && s_instance && s_instance->m_targetWnd) {
        const auto* kb = reinterpret_cast<KBDLLHOOKSTRUCT*>(lParam);
        if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN) {
            // PostMessage のみ: フックプロシージャは最速で返す
            PostMessageW(s_instance->m_targetWnd,
                         WM_HOTKEY_RAW_KBD,
                         static_cast<WPARAM>(kb->vkCode),
                         static_cast<LPARAM>(kb->scanCode));
        }
    }
    return CallNextHookEx(nullptr, nCode, wParam, lParam);
}

LRESULT CALLBACK HookEngine::mouseProc(
    int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode == HC_ACTION && s_instance && s_instance->m_targetWnd) {
        const auto* ms = reinterpret_cast<MSLLHOOKSTRUCT*>(lParam);
        if (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN ||
            wParam == WM_MBUTTONDOWN || wParam == WM_XBUTTONDOWN ||
            wParam == WM_MOUSEWHEEL  || wParam == WM_MOUSEHWHEEL) {
            PostMessageW(s_instance->m_targetWnd,
                         WM_HOTKEY_RAW_MOUSE,
                         wParam,
                         static_cast<LPARAM>(ms->mouseData));
        }
    }
    return CallNextHookEx(nullptr, nCode, wParam, lParam);
}

// --- フックスレッド ---

void HookEngine::hookThreadProc(std::stop_token stopToken, HWND targetWnd, HookMode mode) {
    s_instance    = this;
    m_targetWnd   = targetWnd;

    // WH_KEYBOARD_LL / WH_MOUSE_LL は calling thread のメッセージループが必要
    wil::unique_hhook kbHook;
    wil::unique_hhook msHook;

    if (mode == HookMode::LowLevel || mode == HookMode::Auto) {
        kbHook.reset(SetWindowsHookExW(
            WH_KEYBOARD_LL, keyboardProc, nullptr, 0));
        msHook.reset(SetWindowsHookExW(
            WH_MOUSE_LL, mouseProc, nullptr, 0));
    }

    // フックスレッドのメッセージループ
    // stop_token が要求されるまで WaitMessage でアイドル
    MSG msg{};
    while (!stopToken.stop_requested()) {
        if (MsgWaitForMultipleObjects(0, nullptr, FALSE, 100, QS_ALLINPUT)
            == WAIT_OBJECT_0)
        {
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        }
    }

    // フック解除はデストラクタ（wil::unique_hhook）で自動実行
    s_instance  = nullptr;
    m_targetWnd = nullptr;
}

// --- 公開 API ---

std::expected<void, HookError>
HookEngine::start(HWND targetWnd, HookMode mode, HotkeyCallback callback) {
    if (m_running) return std::unexpected(HookError::AlreadyRunning);

    m_callback = std::move(callback);

    m_thread = std::jthread([this, targetWnd, mode](std::stop_token st) {
        hookThreadProc(std::move(st), targetWnd, mode);
    });

    m_running = true;
    return {};
}

void HookEngine::stop() noexcept {
    if (!m_running) return;
    m_thread.request_stop();
    if (m_thread.joinable()) m_thread.join();
    m_running = false;
}
