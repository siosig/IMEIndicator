/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - プロセス/サービス管理コマンド実装
*/

#include "ProcessCommands.h"
#include <shellapi.h>
#include <string>
#include <wil/resource.h>

namespace {

struct ProcessErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "ProcessError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<ProcessError>(ev)) {
        case ProcessError::AccessDenied:    return "Access denied";
        case ProcessError::ProcessNotFound: return "Process not found";
        case ProcessError::ApiCallFailed:   return "Process API call failed";
        case ProcessError::InvalidParam:    return "Invalid parameter";
        }
        return "Unknown ProcessError";
    }
};

const ProcessErrorCategory& processErrorCategory() noexcept {
    static ProcessErrorCategory cat;
    return cat;
}

} // namespace

std::error_code make_error_code(ProcessError e) {
    return {static_cast<int>(e), processErrorCategory()};
}

std::expected<void, ProcessError>
launchApp(std::wstring_view exe, std::wstring_view args,
          std::wstring_view workDir, bool asAdmin) noexcept {
    if (exe.empty()) return std::unexpected(ProcessError::InvalidParam);

    const std::wstring exeStr(exe);
    const std::wstring argsStr(args);
    const std::wstring dirStr(workDir);

    SHELLEXECUTEINFOW sei{};
    sei.cbSize       = sizeof(SHELLEXECUTEINFOW);
    sei.fMask        = SEE_MASK_NOCLOSEPROCESS;
    sei.lpVerb       = asAdmin ? L"runas" : L"open";
    sei.lpFile       = exeStr.c_str();
    sei.lpParameters = argsStr.empty() ? nullptr : argsStr.c_str();
    sei.lpDirectory  = dirStr.empty()  ? nullptr : dirStr.c_str();
    sei.nShow        = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&sei)) {
        const DWORD err = GetLastError();
        if (err == ERROR_ACCESS_DENIED || err == ERROR_ELEVATION_REQUIRED) {
            return std::unexpected(ProcessError::AccessDenied);
        }
        return std::unexpected(ProcessError::ApiCallFailed);
    }
    if (sei.hProcess) CloseHandle(sei.hProcess);
    return {};
}

std::expected<void, ProcessError> killForegroundProcess() noexcept {
    HWND hw = GetForegroundWindow();
    if (!hw) return std::unexpected(ProcessError::ProcessNotFound);

    DWORD pid = 0;
    GetWindowThreadProcessId(hw, &pid);
    if (pid == 0) return std::unexpected(ProcessError::ProcessNotFound);

    wil::unique_handle hProc(
        OpenProcess(PROCESS_TERMINATE, FALSE, pid));
    if (!hProc) return std::unexpected(ProcessError::AccessDenied);

    if (!TerminateProcess(hProc.get(), 1)) {
        return std::unexpected(ProcessError::ApiCallFailed);
    }
    return {};
}

std::expected<void, ProcessError>
setForegroundProcessPriority(DWORD priorityClass) noexcept {
    HWND hw = GetForegroundWindow();
    if (!hw) return std::unexpected(ProcessError::ProcessNotFound);

    DWORD pid = 0;
    GetWindowThreadProcessId(hw, &pid);
    if (pid == 0) return std::unexpected(ProcessError::ProcessNotFound);

    wil::unique_handle hProc(
        OpenProcess(PROCESS_SET_INFORMATION, FALSE, pid));
    if (!hProc) return std::unexpected(ProcessError::AccessDenied);

    if (!SetPriorityClass(hProc.get(), priorityClass)) {
        return std::unexpected(ProcessError::ApiCallFailed);
    }
    return {};
}

std::expected<void, ProcessError>
executeProcessCommand(int cmdId, std::wstring_view param) noexcept {
    switch (cmdId) {
    case 11: return killForegroundProcess();
    case 20: return launchApp(param, L"", L"", false);
    case 46: return setForegroundProcessPriority(REALTIME_PRIORITY_CLASS);
    case 47: return setForegroundProcessPriority(HIGH_PRIORITY_CLASS);
    case 48: return setForegroundProcessPriority(NORMAL_PRIORITY_CLASS);
    case 49: return setForegroundProcessPriority(IDLE_PRIORITY_CLASS);
    case 59: return launchApp(param, L"", L"", true);   // 管理者として実行
    case 60: return launchApp(param, L"", L"", false);
    default:
        return std::unexpected(ProcessError::InvalidParam);
    }
}
