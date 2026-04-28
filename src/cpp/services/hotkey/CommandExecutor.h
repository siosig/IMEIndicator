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
 HotkeyP モダン再設計 - コマンドエグゼキュータ インターフェース
 内部コマンド ID 0〜120（HotkeyP 由来）と 200〜299（IMEIndicator 拡張）のディスパッチ。
 121〜199 は予約レンジ（HotkeyP 側で将来拡張があった場合の移植余地）。
*/

#include <string_view>
#include <expected>
#include <system_error>
#include "../../models/hotkey/HotKeyEntry.h"


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// コマンド実行エラー
enum class ExecuteError {
    InvalidCommand,    // コマンド ID が範囲外
    PlatformNotSupported,  // Windows 11 未満で Win11 専用コマンドを実行
    AccessDenied,      // 管理者権限が必要
    ProcessLaunchFailed,   // プロセス起動失敗
    ApiCallFailed,     // Win32 API 呼び出し失敗
};

std::error_code make_error_code(ExecuteError e);

// コマンド実行インターフェース
// param: コマンドパラメータ文字列（コマンドによって意味が異なる）
// hk:   トリガーとなったホットキーエントリ（nullptr 可）
[[nodiscard]] std::expected<void, ExecuteError>
executeCommand(Command cmd, std::wstring_view param, const HotKeyEntry* hk);

// 整数 ID からコマンドを実行（既存コードとの互換性のため）
[[nodiscard]] std::expected<void, ExecuteError>
executeCommandById(int cmdId, std::wstring_view param, const HotKeyEntry* hk);

// コマンド ID の有効性チェック（HotkeyP 由来 [0, 120] + IMEIndicator 拡張 [200, 299]）。
// 121〜199 は予約レンジで無効扱い。300〜 は未定義。
[[nodiscard]] constexpr bool isValidCommandId(int id) noexcept {
    return (id >= 0 && id <= 120) || (id >= 200 && id <= 299);
}

// コマンドの表示名を取得
[[nodiscard]] const wchar_t* getCommandName(Command cmd) noexcept;
[[nodiscard]] const wchar_t* getCommandNameById(int cmdId) noexcept;

// メインウィンドウハンドルを設定（UI 層から呼び出す）
void setMainWindowHandle(HWND hwnd) noexcept;

} // namespace imeindicator::services::hotkey
