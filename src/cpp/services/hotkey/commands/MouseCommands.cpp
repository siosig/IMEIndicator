/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - マウス操作シミュレーションコマンド実装
 SendInput を使用してマウスイベントを送信。
*/

#include "MouseCommands.h"


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

namespace {

struct MouseErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "MouseError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<MouseError>(ev)) {
        case MouseError::ApiCallFailed: return "Mouse API call failed";
        case MouseError::InvalidParam:  return "Invalid parameter";
        }
        return "Unknown MouseError";
    }
};

const MouseErrorCategory& mouseErrorCategory() noexcept {
    static MouseErrorCategory cat;
    return cat;
}

} // namespace

std::error_code make_error_code(MouseError e) {
    return {static_cast<int>(e), mouseErrorCategory()};
}

std::expected<void, MouseError> moveMouse(int x, int y) noexcept {
    // 絶対座標は MOUSEEVENTF_ABSOLUTE フラグを使用し
    // 0〜65535 の正規化座標に変換する
    const int screenW = GetSystemMetrics(SM_CXSCREEN);
    const int screenH = GetSystemMetrics(SM_CYSCREEN);
    if (screenW <= 0 || screenH <= 0) {
        return std::unexpected(MouseError::ApiCallFailed);
    }

    const LONG normX = static_cast<LONG>(x * 65535 / screenW);
    const LONG normY = static_cast<LONG>(y * 65535 / screenH);

    INPUT inp{};
    inp.type         = INPUT_MOUSE;
    inp.mi.dx        = normX;
    inp.mi.dy        = normY;
    inp.mi.dwFlags   = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

    if (SendInput(1, &inp, sizeof(INPUT)) == 0) {
        return std::unexpected(MouseError::ApiCallFailed);
    }
    return {};
}

std::expected<void, MouseError> moveMouseRelative(int dx, int dy) noexcept {
    INPUT inp{};
    inp.type       = INPUT_MOUSE;
    inp.mi.dx      = static_cast<LONG>(dx);
    inp.mi.dy      = static_cast<LONG>(dy);
    inp.mi.dwFlags = MOUSEEVENTF_MOVE;

    if (SendInput(1, &inp, sizeof(INPUT)) == 0) {
        return std::unexpected(MouseError::ApiCallFailed);
    }
    return {};
}

std::expected<void, MouseError>
mouseClick(DWORD buttonDownFlag, DWORD buttonUpFlag) noexcept {
    INPUT inputs[2]{};
    inputs[0].type         = INPUT_MOUSE;
    inputs[0].mi.dwFlags   = buttonDownFlag;
    inputs[1].type         = INPUT_MOUSE;
    inputs[1].mi.dwFlags   = buttonUpFlag;

    if (SendInput(2, inputs, sizeof(INPUT)) == 0) {
        return std::unexpected(MouseError::ApiCallFailed);
    }
    return {};
}

std::expected<void, MouseError> mouseScroll(int delta) noexcept {
    INPUT inp{};
    inp.type           = INPUT_MOUSE;
    inp.mi.mouseData   = static_cast<DWORD>(delta * WHEEL_DELTA);
    inp.mi.dwFlags     = MOUSEEVENTF_WHEEL;

    if (SendInput(1, &inp, sizeof(INPUT)) == 0) {
        return std::unexpected(MouseError::ApiCallFailed);
    }
    return {};
}

std::expected<void, MouseError> executeMouseCommand(int cmdId) noexcept {
    switch (cmdId) {
    case 37:  return mouseClick(MOUSEEVENTF_LEFTDOWN,   MOUSEEVENTF_LEFTUP);
    case 43:  return mouseClick(MOUSEEVENTF_LEFTDOWN,   MOUSEEVENTF_LEFTUP);
    case 44:  return mouseClick(MOUSEEVENTF_RIGHTDOWN,  MOUSEEVENTF_RIGHTUP);
    case 45:  return mouseClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
    case 75:  return mouseScroll(3);    // スクロールアップ
    case 76:  return mouseScroll(-3);   // スクロールダウン
    case 79:  return moveMouseRelative(-10, 0);
    case 80:  return moveMouseRelative( 10, 0);
    case 108: return mouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
    default:
        return std::unexpected(MouseError::InvalidParam);
    }
}

} // namespace imeindicator::services::hotkey
