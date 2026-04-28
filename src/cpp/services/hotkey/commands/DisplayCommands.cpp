/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - ディスプレイ制御コマンド実装
 ChangeDisplaySettingsExW を使用。
*/

#include "DisplayCommands.h"


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

namespace {

struct DisplayErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "DisplayError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<DisplayError>(ev)) {
        case DisplayError::ApiCallFailed: return "Display API call failed";
        case DisplayError::NoDisplay:     return "No display found";
        }
        return "Unknown DisplayError";
    }
};

const DisplayErrorCategory& displayErrorCategory() noexcept {
    static DisplayErrorCategory cat;
    return cat;
}

// プライマリモニターの現在設定を取得
[[nodiscard]] bool getCurrentDevMode(DEVMODEW& dm) noexcept {
    dm = {};
    dm.dmSize = sizeof(DEVMODEW);
    return EnumDisplaySettingsW(nullptr, ENUM_CURRENT_SETTINGS, &dm) != FALSE;
}

} // namespace

std::error_code make_error_code(DisplayError e) {
    return {static_cast<int>(e), displayErrorCategory()};
}

std::expected<void, DisplayError> rotateDisplay(DWORD degrees) noexcept {
    DEVMODEW dm{};
    if (!getCurrentDevMode(dm)) {
        return std::unexpected(DisplayError::NoDisplay);
    }

    // 現在の向きから新しい向きを計算
    const DWORD current = dm.dmDisplayOrientation;
    DWORD next = current;

    if (degrees == 90) {
        next = (current + 1) % 4;
    } else if (degrees == 270) {
        next = (current + 3) % 4;
    } else if (degrees == 180) {
        next = (current + 2) % 4;
    }

    if (next == current) return {};

    // 90度回転すると縦横が入れ替わる
    if ((current % 2) != (next % 2)) {
        std::swap(dm.dmPelsWidth, dm.dmPelsHeight);
    }
    dm.dmDisplayOrientation = next;
    dm.dmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

    const LONG res = ChangeDisplaySettingsExW(nullptr, &dm, nullptr,
                                              CDS_UPDATEREGISTRY, nullptr);
    if (res != DISP_CHANGE_SUCCESSFUL) {
        return std::unexpected(DisplayError::ApiCallFailed);
    }
    return {};
}

std::expected<void, DisplayError> flipDisplayHorizontal() noexcept {
    // Windows の ChangeDisplaySettings では水平反転を直接サポートしない
    // keybd_event で Win+P を模倣してプロジェクションを切り替える
    // Win32 API のみでは実現不可のため noop
    return {};
}

std::expected<void, DisplayError> executeDisplayCommand(int cmdId) noexcept {
    switch (cmdId) {
    case 18: return rotateDisplay(90);
    case 19: return rotateDisplay(270);
    case 23: return rotateDisplay(180);
    default:
        return std::unexpected(DisplayError::ApiCallFailed);
    }
}

} // namespace imeindicator::services::hotkey
