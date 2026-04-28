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
 HotkeyP モダン再設計 - CD/メディアコマンド
 コマンド 0〜1・39〜42・100
*/

#include <windows.h>
#include <expected>
#include <string_view>
#include <system_error>


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

enum class MediaError {
    ApiCallFailed,
    DeviceNotFound,
};

std::error_code make_error_code(MediaError e);

// CDトレイをイジェクト
[[nodiscard]] std::expected<void, MediaError>
ejectCD(std::wstring_view driveLetter) noexcept;

// CDトレイを閉じる
[[nodiscard]] std::expected<void, MediaError>
closeCD(std::wstring_view driveLetter) noexcept;

// メディアキーを送信（Play/Pause/Stop/Next/Prev）
[[nodiscard]] std::expected<void, MediaError>
sendMediaKey(WORD vk) noexcept;

// メディアコマンドを実行
[[nodiscard]] std::expected<void, MediaError>
executeMediaCommand(int cmdId, std::wstring_view param) noexcept;

} // namespace imeindicator::services::hotkey
