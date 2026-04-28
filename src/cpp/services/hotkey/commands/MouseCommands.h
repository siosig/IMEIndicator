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
 HotkeyP モダン再設計 - マウス操作シミュレーションコマンド
 コマンド 37・43〜45・75〜76・79〜80・108
*/

#include <windows.h>
#include <expected>
#include <system_error>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

enum class MouseError {
    ApiCallFailed,
    InvalidParam,
};

std::error_code make_error_code(MouseError e);

// マウスカーソルを移動（絶対座標）
[[nodiscard]] std::expected<void, MouseError>
moveMouse(int x, int y) noexcept;

// マウスカーソルを移動（相対座標）
[[nodiscard]] std::expected<void, MouseError>
moveMouseRelative(int dx, int dy) noexcept;

// マウスクリック
[[nodiscard]] std::expected<void, MouseError>
mouseClick(DWORD buttonDownFlag, DWORD buttonUpFlag) noexcept;

// マウスホイールをスクロール（delta: 正=上、負=下）
[[nodiscard]] std::expected<void, MouseError>
mouseScroll(int delta) noexcept;

// マウスコマンドを実行
[[nodiscard]] std::expected<void, MouseError>
executeMouseCommand(int cmdId) noexcept;

} // namespace imeindicator::services::hotkey
