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
 HotkeyP モダン再設計 - 電源管理コマンド
 ShutdownW / InitiateShutdownW を使用。SeShutdownPrivilege を適切に取得。
*/

#include <windows.h>
#include <expected>
#include <system_error>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// 電源操作エラー
enum class PowerError {
    PrivilegeNotHeld,  // SeShutdownPrivilege を取得できなかった
    ApiCallFailed,     // Win32 API 呼び出し失敗
};

std::error_code make_error_code(PowerError e);

// シャットダウン
[[nodiscard]] std::expected<void, PowerError>
shutdownSystem() noexcept;

// 再起動
[[nodiscard]] std::expected<void, PowerError>
rebootSystem() noexcept;

// ログオフ
[[nodiscard]] std::expected<void, PowerError>
logoffUser() noexcept;

// 画面ロック
[[nodiscard]] std::expected<void, PowerError>
lockWorkstation() noexcept;

// スリープ（サスペンド）
[[nodiscard]] std::expected<void, PowerError>
sleepSystem() noexcept;

// 休止状態（ハイバネート）
[[nodiscard]] std::expected<void, PowerError>
hibernateSystem() noexcept;

// モニター電源オフ
[[nodiscard]] std::expected<void, PowerError>
turnOffMonitor() noexcept;

// スクリーンセーバーを起動
[[nodiscard]] std::expected<void, PowerError>
startScreenSaver() noexcept;

} // namespace imeindicator::services::hotkey
