/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - ウィンドウ操作コマンド実装
*/

#include "WindowCommands.h"
#include <algorithm>

namespace {

struct WindowErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override {
        return "WindowError";
    }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<WindowError>(ev)) {
        case WindowError::NoWindow:      return "No target window";
        case WindowError::ApiCallFailed: return "Window API call failed";
        }
        return "Unknown WindowError";
    }
};

const WindowErrorCategory& windowErrorCategory() noexcept {
    static WindowErrorCategory cat;
    return cat;
}

[[nodiscard]] HWND getForegroundOrNull() noexcept {
    HWND hw = GetForegroundWindow();
    if (!hw) return nullptr;
    // デスクトップ自体は対象外
    HWND desktop = GetDesktopWindow();
    return (hw == desktop) ? nullptr : hw;
}

} // namespace

std::error_code make_error_code(WindowError e) {
    return {static_cast<int>(e), windowErrorCategory()};
}

std::expected<void, WindowError> minimizeActiveWindow() noexcept {
    HWND hw = getForegroundOrNull();
    if (!hw) return std::unexpected(WindowError::NoWindow);
    ShowWindow(hw, SW_MINIMIZE);
    return {};
}

std::expected<void, WindowError> maximizeActiveWindow() noexcept {
    HWND hw = getForegroundOrNull();
    if (!hw) return std::unexpected(WindowError::NoWindow);

    const WINDOWPLACEMENT wp{ sizeof(WINDOWPLACEMENT) };
    if (!GetWindowPlacement(hw, const_cast<WINDOWPLACEMENT*>(&wp))) {
        return std::unexpected(WindowError::ApiCallFailed);
    }
    // 最大化中なら元に戻す、そうでなければ最大化
    ShowWindow(hw,
        wp.showCmd == SW_SHOWMAXIMIZED ? SW_RESTORE : SW_MAXIMIZE);
    return {};
}

std::expected<void, WindowError> closeActiveWindow() noexcept {
    HWND hw = getForegroundOrNull();
    if (!hw) return std::unexpected(WindowError::NoWindow);
    PostMessageW(hw, WM_CLOSE, 0, 0);
    return {};
}

std::expected<void, WindowError> minimizeToTray(HWND mainHwnd) noexcept {
    if (!mainHwnd) return std::unexpected(WindowError::NoWindow);
    ShowWindow(mainHwnd, SW_MINIMIZE);
    return {};
}

std::expected<void, WindowError> showDesktop() noexcept {
    // Shell の「デスクトップを表示」機能を呼び出す
    HWND trayWnd = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!trayWnd) return std::unexpected(WindowError::NoWindow);
    SendMessageW(trayWnd, WM_COMMAND, static_cast<WPARAM>(407), 0);
    return {};
}

std::expected<void, WindowError> toggleAlwaysOnTop() noexcept {
    HWND hw = getForegroundOrNull();
    if (!hw) return std::unexpected(WindowError::NoWindow);

    const LONG_PTR exStyle = GetWindowLongPtrW(hw, GWL_EXSTYLE);
    const bool isTopmost   = (exStyle & WS_EX_TOPMOST) != 0;

    SetWindowPos(hw,
        isTopmost ? HWND_NOTOPMOST : HWND_TOPMOST,
        0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE);
    return {};
}

std::expected<void, WindowError> setWindowOpacity(int opacity) noexcept {
    HWND hw = getForegroundOrNull();
    if (!hw) return std::unexpected(WindowError::NoWindow);

    const int clamped = std::clamp(opacity, 0, 255);

    // WS_EX_LAYERED が必要
    LONG_PTR exStyle = GetWindowLongPtrW(hw, GWL_EXSTYLE);
    if (!(exStyle & WS_EX_LAYERED)) {
        SetWindowLongPtrW(hw, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
    }

    // opacity=0 は完全不透明（255）として扱う（HotkeyP の慣例）
    const BYTE alpha = (clamped == 0) ? 255
                     : static_cast<BYTE>(255 - clamped);
    if (!SetLayeredWindowAttributes(hw, 0, alpha, LWA_ALPHA)) {
        return std::unexpected(WindowError::ApiCallFailed);
    }
    return {};
}

std::expected<void, WindowError> centerWindow() noexcept {
    HWND hw = getForegroundOrNull();
    if (!hw) return std::unexpected(WindowError::NoWindow);

    RECT wndRect{};
    if (!GetWindowRect(hw, &wndRect)) {
        return std::unexpected(WindowError::ApiCallFailed);
    }

    // ウィンドウが属するモニターを取得
    HMONITOR hmon = MonitorFromWindow(hw, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{ sizeof(MONITORINFO) };
    if (!GetMonitorInfoW(hmon, &mi)) {
        return std::unexpected(WindowError::ApiCallFailed);
    }

    const int wndW = wndRect.right  - wndRect.left;
    const int wndH = wndRect.bottom - wndRect.top;
    const int monW = mi.rcWork.right  - mi.rcWork.left;
    const int monH = mi.rcWork.bottom - mi.rcWork.top;

    const int x = mi.rcWork.left + (monW - wndW) / 2;
    const int y = mi.rcWork.top  + (monH - wndH) / 2;

    SetWindowPos(hw, nullptr, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    return {};
}

std::expected<void, WindowError> switchToNextWindow() noexcept {
    // Alt+Tab 相当: GetWindow で次のウィンドウに切り替え
    keybd_event(VK_MENU, 0, 0, 0);
    keybd_event(VK_TAB,  0, 0, 0);
    keybd_event(VK_TAB,  0, KEYEVENTF_KEYUP, 0);
    keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
    return {};
}

std::expected<void, WindowError>
executeWindowCommand(int cmdId, std::wstring_view /*param*/, HWND mainHwnd) noexcept {
    switch (cmdId) {
    case 7:   return minimizeActiveWindow();
    case 8:   return maximizeActiveWindow();
    case 9:   return closeActiveWindow();
    case 10:  return showDesktop();
    case 25:  return toggleAlwaysOnTop();
    case 65:  return minimizeToTray(mainHwnd);
    case 77:  return centerWindow();
    case 90:  return switchToNextWindow();
    case 115: return minimizeToTray(mainHwnd);
    default:
        return std::unexpected(WindowError::ApiCallFailed);
    }
}
