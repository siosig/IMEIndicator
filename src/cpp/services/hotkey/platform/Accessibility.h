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
 HotkeyP モダン再設計 - Accessibility（トレイアイコン調査）
 IAccessible を使ってシステムトレイアイコンの名前・位置を取得。
 spy.exe の機能を EXE 内に統合。
*/

#include <windows.h>
#include <expected>
#include <string>
#include <system_error>
#include <vector>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// トレイアイコン情報
struct TrayIconInfo {
    std::wstring name;   // アイコン名（ツールチップテキスト）
    int x      = 0;
    int y      = 0;
    int width  = 0;
    int height = 0;
};

// Accessibility エラー
enum class AccessibilityError {
    WindowNotFound,  // Shell_TrayWnd / SysPager が見つからない
    ComError,        // IAccessible COM エラー
    NoIcons,         // アイコンが存在しない
};

std::error_code make_error_code(AccessibilityError e);

// システムトレイのアイコン一覧を取得
// Shell_TrayWnd → TrayNotifyWnd → SysPager → ToolbarWindow32 を辿る
[[nodiscard]] std::expected<std::vector<TrayIconInfo>, AccessibilityError>
enumerateTrayIcons();

// 指定名のトレイアイコンをクリック
[[nodiscard]] std::expected<void, AccessibilityError>
clickTrayIcon(const TrayIconInfo& icon) noexcept;

} // namespace imeindicator::services::hotkey
