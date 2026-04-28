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
 HotkeyP モダン再設計 - ウィンドウ操作コマンド
 コマンド 7〜10・25・65・77・90・101・102・110〜112・115
*/

#include <windows.h>
#include <expected>
#include <string_view>
#include <system_error>


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

enum class WindowError {
    NoWindow,        // 対象ウィンドウが存在しない
    ApiCallFailed,   // Win32 API 呼び出し失敗
};

std::error_code make_error_code(WindowError e);

// アクティブウィンドウを最小化
[[nodiscard]] std::expected<void, WindowError>
minimizeActiveWindow() noexcept;

// アクティブウィンドウを最大化/元のサイズに戻す（トグル）
[[nodiscard]] std::expected<void, WindowError>
maximizeActiveWindow() noexcept;

// アクティブウィンドウを閉じる
[[nodiscard]] std::expected<void, WindowError>
closeActiveWindow() noexcept;

// アクティブウィンドウを最小化してトレイに格納
[[nodiscard]] std::expected<void, WindowError>
minimizeToTray(HWND mainHwnd) noexcept;

// すべてのウィンドウを最小化（デスクトップを表示）
[[nodiscard]] std::expected<void, WindowError>
showDesktop() noexcept;

// アクティブウィンドウを常に手前に表示するトグル
[[nodiscard]] std::expected<void, WindowError>
toggleAlwaysOnTop() noexcept;

// アクティブウィンドウの透明度を設定（0=不透明〜255=完全透明）
[[nodiscard]] std::expected<void, WindowError>
setWindowOpacity(int opacity) noexcept;

// アクティブウィンドウを画面中央に移動
[[nodiscard]] std::expected<void, WindowError>
centerWindow() noexcept;

// 次のウィンドウにフォーカスを移動（Alt+Tab 相当）
[[nodiscard]] std::expected<void, WindowError>
switchToNextWindow() noexcept;

// ウィンドウコマンドをパラメータ文字列から実行
// param 形式: コマンドIDに対応する文字列
[[nodiscard]] std::expected<void, WindowError>
executeWindowCommand(int cmdId, std::wstring_view param, HWND mainHwnd) noexcept;

} // namespace imeindicator::services::hotkey
