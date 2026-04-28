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
 HotkeyP モダン再設計 - ディスプレイ制御コマンド
 コマンド 18〜19・23・68〜69・81〜82・89〜90・103・106〜107
*/

#include <windows.h>
#include <expected>
#include <system_error>

enum class DisplayError {
    ApiCallFailed,
    NoDisplay,
};

std::error_code make_error_code(DisplayError e);

// 画面の解像度・リフレッシュレートを変更
[[nodiscard]] std::expected<void, DisplayError>
rotateDisplay(DWORD degrees) noexcept;

// 画面を反転（左右）
[[nodiscard]] std::expected<void, DisplayError>
flipDisplayHorizontal() noexcept;

// ディスプレイコマンドを実行
[[nodiscard]] std::expected<void, DisplayError>
executeDisplayCommand(int cmdId) noexcept;
