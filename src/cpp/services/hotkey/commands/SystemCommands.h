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
 HotkeyP モダン再設計 - その他システムコマンド
 コマンド 6・12・22・24・61・66・70・85〜88・95〜99・104〜105・109・113〜114
*/

#include <windows.h>
#include <expected>
#include <string_view>
#include <system_error>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

enum class SystemCmdError {
    ApiCallFailed,
    InvalidParam,
};

std::error_code make_error_code(SystemCmdError e);

// タスクマネージャーを開く
[[nodiscard]] std::expected<void, SystemCmdError>
openTaskManager() noexcept;

// コントロールパネルを開く
[[nodiscard]] std::expected<void, SystemCmdError>
openControlPanel() noexcept;

// 設定アプリを開く（Windows 11）
[[nodiscard]] std::expected<void, SystemCmdError>
openSettings(std::wstring_view page) noexcept;

// システムサウンドを再生
[[nodiscard]] std::expected<void, SystemCmdError>
playSoundFile(std::wstring_view path) noexcept;

// URL をデフォルトブラウザで開く
[[nodiscard]] std::expected<void, SystemCmdError>
openUrl(std::wstring_view url) noexcept;

// クリップボードをクリア
[[nodiscard]] std::expected<void, SystemCmdError>
clearClipboard() noexcept;

// スクリーンショットをクリップボードにコピー
[[nodiscard]] std::expected<void, SystemCmdError>
captureScreen() noexcept;

// システムコマンドを実行
[[nodiscard]] std::expected<void, SystemCmdError>
executeSystemCommand(int cmdId, std::wstring_view param) noexcept;

} // namespace imeindicator::services::hotkey
