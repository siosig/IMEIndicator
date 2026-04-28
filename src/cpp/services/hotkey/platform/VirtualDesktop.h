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
 HotkeyP モダン再設計 - VirtualDesktop コマンド（Windows 11 統合）
 コマンド 116〜119: 仮想デスクトップの切り替え/作成/削除
 Windows 10 以下では SendInput フォールバック（Win+Ctrl+Arrow/D/F4）
*/

#include <windows.h>
#include <expected>
#include <system_error>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// VirtualDesktop コマンド ID
enum class VirtualDesktopCommand : int {
    Next         = 116,
    Prev         = 117,
    CreateNew    = 118,
    CloseCurrent = 119,
};

// VirtualDesktop エラー
enum class VirtualDesktopError {
    NotSupported,   // Windows 11 未満
    ComError,       // COM インターフェース失敗
    NoDesktops,     // デスクトップが存在しない
};

std::error_code make_error_code(VirtualDesktopError e);

// VirtualDesktopManager
class VirtualDesktopManager {
public:
    VirtualDesktopManager() = delete;

    // Windows 11 (ビルド 22000+) かどうか確認
    [[nodiscard]] static bool isWindows11OrLater() noexcept;

    // --- フォールバック実装（SendInput による仮想キー送信）---
    // Windows 10 以下 / COM が使えない場合に使用

    // 次の仮想デスクトップ (Win+Ctrl+Right)
    [[nodiscard]] static std::expected<void, VirtualDesktopError>
    switchNextFallback() noexcept;

    // 前の仮想デスクトップ (Win+Ctrl+Left)
    [[nodiscard]] static std::expected<void, VirtualDesktopError>
    switchPrevFallback() noexcept;

    // 新規仮想デスクトップ作成 (Win+Ctrl+D)
    [[nodiscard]] static std::expected<void, VirtualDesktopError>
    createNewFallback() noexcept;

    // 現在の仮想デスクトップを閉じる (Win+Ctrl+F4)
    [[nodiscard]] static std::expected<void, VirtualDesktopError>
    closeCurrentFallback() noexcept;

    // コマンドを実行（Windows 11 なら COM 経由、それ以外はフォールバック）
    [[nodiscard]] static std::expected<void, VirtualDesktopError>
    execute(VirtualDesktopCommand cmd) noexcept;
};

} // namespace imeindicator::services::hotkey
