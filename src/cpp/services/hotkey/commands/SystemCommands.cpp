/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - その他システムコマンド実装
*/

#include "SystemCommands.h"
#include <shellapi.h>
#include <mmsystem.h>
#include <string>


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

#pragma comment(lib, "winmm.lib")

namespace {

struct SystemCmdErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "SystemCmdError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<SystemCmdError>(ev)) {
        case SystemCmdError::ApiCallFailed: return "System API call failed";
        case SystemCmdError::InvalidParam:  return "Invalid parameter";
        }
        return "Unknown SystemCmdError";
    }
};

const SystemCmdErrorCategory& systemCmdErrorCategory() noexcept {
    static SystemCmdErrorCategory cat;
    return cat;
}

[[nodiscard]] std::expected<void, SystemCmdError>
shellOpen(const wchar_t* target, const wchar_t* params = nullptr) noexcept {
    const auto result = reinterpret_cast<INT_PTR>(
        ShellExecuteW(nullptr, L"open", target, params, nullptr, SW_SHOWNORMAL));
    if (result <= 32) return std::unexpected(SystemCmdError::ApiCallFailed);
    return {};
}

} // namespace

std::error_code make_error_code(SystemCmdError e) {
    return {static_cast<int>(e), systemCmdErrorCategory()};
}

std::expected<void, SystemCmdError> openTaskManager() noexcept {
    return shellOpen(L"taskmgr.exe");
}

std::expected<void, SystemCmdError> openControlPanel() noexcept {
    return shellOpen(L"control.exe");
}

std::expected<void, SystemCmdError> openSettings(std::wstring_view page) noexcept {
    // ms-settings: URI スキームを使用
    std::wstring uri = L"ms-settings:";
    if (!page.empty()) uri += page;
    return shellOpen(uri.c_str());
}

std::expected<void, SystemCmdError> playSoundFile(std::wstring_view path) noexcept {
    if (path.empty()) {
        // システムビープ
        MessageBeep(MB_OK);
        return {};
    }
    const std::wstring pathStr(path);
    if (!PlaySoundW(pathStr.c_str(), nullptr,
                    SND_FILENAME | SND_ASYNC | SND_NODEFAULT)) {
        return std::unexpected(SystemCmdError::ApiCallFailed);
    }
    return {};
}

std::expected<void, SystemCmdError> openUrl(std::wstring_view url) noexcept {
    if (url.empty()) return std::unexpected(SystemCmdError::InvalidParam);
    const std::wstring urlStr(url);
    return shellOpen(urlStr.c_str());
}

std::expected<void, SystemCmdError> clearClipboard() noexcept {
    if (!OpenClipboard(nullptr)) {
        return std::unexpected(SystemCmdError::ApiCallFailed);
    }
    EmptyClipboard();
    CloseClipboard();
    return {};
}

std::expected<void, SystemCmdError> captureScreen() noexcept {
    // Print Screen キーを送信
    INPUT inputs[2]{};
    inputs[0].type   = INPUT_KEYBOARD;
    inputs[0].ki.wVk = VK_SNAPSHOT;
    inputs[1].type   = INPUT_KEYBOARD;
    inputs[1].ki.wVk = VK_SNAPSHOT;
    inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;

    if (SendInput(2, inputs, sizeof(INPUT)) == 0) {
        return std::unexpected(SystemCmdError::ApiCallFailed);
    }
    return {};
}

std::expected<void, SystemCmdError>
executeSystemCommand(int cmdId, std::wstring_view param) noexcept {
    switch (cmdId) {
    case 6:   return openTaskManager();
    case 12:  return openControlPanel();
    case 22:  return playSoundFile(param);
    case 24:  return openUrl(param);
    case 61:  return clearClipboard();
    case 66:  return captureScreen();
    case 70:  return openSettings(param);
    case 85:  return openSettings(L"display");
    case 86:  return openSettings(L"sound");
    case 87:  return openSettings(L"personalization");
    case 88:  return openSettings(L"network-status");
    case 95:  return shellOpen(L"explorer.exe");
    case 96:  return shellOpen(L"notepad.exe");
    case 97:  return shellOpen(L"calc.exe");
    case 98:  return shellOpen(L"mspaint.exe");
    case 99:  return shellOpen(L"cmd.exe");
    case 104: return openSettings(L"windowsupdate");
    case 105: return openSettings(L"appsfeatures");
    case 109: return shellOpen(param.empty() ? L"explorer.exe"
                                             : std::wstring(param).c_str());
    case 113: return openSettings(L"printers");
    case 114: return openSettings(L"bluetooth");
    default:
        return std::unexpected(SystemCmdError::InvalidParam);
    }
}

} // namespace imeindicator::services::hotkey
