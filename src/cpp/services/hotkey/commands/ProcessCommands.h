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
 HotkeyP モダン再設計 - プロセス/サービス管理コマンド
 コマンド 11・20・46〜49・59〜60・92〜93
*/

#include <windows.h>
#include <expected>
#include <string_view>
#include <system_error>

enum class ProcessError {
    AccessDenied,
    ProcessNotFound,
    ApiCallFailed,
    InvalidParam,
};

std::error_code make_error_code(ProcessError e);

// アプリケーションを起動（ShellExecuteExW）
[[nodiscard]] std::expected<void, ProcessError>
launchApp(std::wstring_view exe, std::wstring_view args,
          std::wstring_view workDir, bool asAdmin = false) noexcept;

// フォアグラウンドウィンドウのプロセスを終了
[[nodiscard]] std::expected<void, ProcessError>
killForegroundProcess() noexcept;

// プロセスの優先度を設定
[[nodiscard]] std::expected<void, ProcessError>
setForegroundProcessPriority(DWORD priorityClass) noexcept;

// プロセスコマンドを実行
[[nodiscard]] std::expected<void, ProcessError>
executeProcessCommand(int cmdId, std::wstring_view param) noexcept;
