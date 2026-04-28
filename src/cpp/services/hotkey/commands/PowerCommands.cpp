/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - 電源管理コマンド実装
 InitiateShutdownW / ExitWindowsEx / SetSuspendState を使用。
 SeShutdownPrivilege を適切に取得して解放する。
*/

#include "PowerCommands.h"
#include <powrprof.h>
#include <wil/resource.h>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

#pragma comment(lib, "PowrProf.lib")

// --- エラーカテゴリ ---

namespace {

struct PowerErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override {
        return "PowerError";
    }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<PowerError>(ev)) {
        case PowerError::PrivilegeNotHeld: return "SeShutdownPrivilege not held";
        case PowerError::ApiCallFailed:    return "Power API call failed";
        }
        return "Unknown PowerError";
    }
};

const PowerErrorCategory& powerErrorCategory() noexcept {
    static PowerErrorCategory cat;
    return cat;
}

// SeShutdownPrivilege を有効化（RAII スコープ）
[[nodiscard]] bool enableShutdownPrivilege() noexcept {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(),
                          TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                          &token)) {
        return false;
    }
    const auto hToken = wil::unique_handle(token);

    LUID luid{};
    if (!LookupPrivilegeValueW(nullptr, SE_SHUTDOWN_NAME, &luid)) {
        return false;
    }

    TOKEN_PRIVILEGES tp{};
    tp.PrivilegeCount           = 1;
    tp.Privileges[0].Luid       = luid;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;

    return AdjustTokenPrivileges(hToken.get(), FALSE, &tp, 0, nullptr, nullptr)
        && GetLastError() == ERROR_SUCCESS;
}

} // namespace

// --- エラーコード生成 ---

std::error_code make_error_code(PowerError e) {
    return {static_cast<int>(e), powerErrorCategory()};
}

// --- 公開 API ---

std::expected<void, PowerError> shutdownSystem() noexcept {
    if (!enableShutdownPrivilege()) {
        return std::unexpected(PowerError::PrivilegeNotHeld);
    }
    if (!ExitWindowsEx(EWX_SHUTDOWN | EWX_FORCE, SHTDN_REASON_MAJOR_OTHER)) {
        return std::unexpected(PowerError::ApiCallFailed);
    }
    return {};
}

std::expected<void, PowerError> rebootSystem() noexcept {
    if (!enableShutdownPrivilege()) {
        return std::unexpected(PowerError::PrivilegeNotHeld);
    }
    if (!ExitWindowsEx(EWX_REBOOT | EWX_FORCE, SHTDN_REASON_MAJOR_OTHER)) {
        return std::unexpected(PowerError::ApiCallFailed);
    }
    return {};
}

std::expected<void, PowerError> logoffUser() noexcept {
    if (!ExitWindowsEx(EWX_LOGOFF | EWX_FORCE, SHTDN_REASON_MAJOR_OTHER)) {
        return std::unexpected(PowerError::ApiCallFailed);
    }
    return {};
}

std::expected<void, PowerError> lockWorkstation() noexcept {
    if (!LockWorkStation()) {
        return std::unexpected(PowerError::ApiCallFailed);
    }
    return {};
}

std::expected<void, PowerError> sleepSystem() noexcept {
    if (!SetSuspendState(FALSE, FALSE, FALSE)) {
        return std::unexpected(PowerError::ApiCallFailed);
    }
    return {};
}

std::expected<void, PowerError> hibernateSystem() noexcept {
    if (!SetSuspendState(TRUE, FALSE, FALSE)) {
        return std::unexpected(PowerError::ApiCallFailed);
    }
    return {};
}

std::expected<void, PowerError> turnOffMonitor() noexcept {
    // SC_MONITORPOWER: 2=オフ, 1=低電力, -1=オン
    SendMessageW(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER,
                 static_cast<LPARAM>(2));
    return {};
}

std::expected<void, PowerError> startScreenSaver() noexcept {
    SendMessageW(HWND_BROADCAST, WM_SYSCOMMAND, SC_SCREENSAVE, 0);
    return {};
}

} // namespace imeindicator::services::hotkey
