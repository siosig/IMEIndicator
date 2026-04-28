/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - VirtualDesktop コマンド実装
 Windows 11 (ビルド 22000+) では仮想デスクトップ操作を SendInput で実装。
 IVirtualDesktopManager COM インターフェースは非公式 API のため
 フォールバック実装（Win+Ctrl+矢印キー）を使用。
*/

#include "VirtualDesktop.h"
#include <vector>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

namespace {

struct VirtualDesktopErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override {
        return "VirtualDesktopError";
    }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<VirtualDesktopError>(ev)) {
        case VirtualDesktopError::NotSupported: return "Virtual desktops not supported on this OS";
        case VirtualDesktopError::ComError:     return "Virtual desktop COM error";
        case VirtualDesktopError::NoDesktops:   return "No virtual desktops found";
        }
        return "Unknown VirtualDesktopError";
    }
};

const VirtualDesktopErrorCategory& vdErrorCategory() noexcept {
    static VirtualDesktopErrorCategory cat;
    return cat;
}

// SendInput でキーシーケンスを送信
void sendKeySequence(std::initializer_list<WORD> keys) noexcept {
    const size_t count = keys.size();
    std::vector<INPUT> inputs;
    inputs.reserve(count * 2);

    for (WORD vk : keys) {
        INPUT inp{};
        inp.type   = INPUT_KEYBOARD;
        inp.ki.wVk = vk;
        inputs.push_back(inp);
    }
    for (auto it = keys.end(); it != keys.begin(); ) {
        --it;
        INPUT inp{};
        inp.type         = INPUT_KEYBOARD;
        inp.ki.wVk       = *it;
        inp.ki.dwFlags   = KEYEVENTF_KEYUP;
        inputs.push_back(inp);
    }
    SendInput(static_cast<UINT>(inputs.size()), inputs.data(), sizeof(INPUT));
}

} // namespace

std::error_code make_error_code(VirtualDesktopError e) {
    return {static_cast<int>(e), vdErrorCategory()};
}

bool VirtualDesktopManager::isWindows11OrLater() noexcept {
    // RTL バージョン情報で Windows 11 (ビルド 22000+) を判定
    OSVERSIONINFOEXW osvi{};
    osvi.dwOSVersionInfoSize = sizeof(OSVERSIONINFOEXW);
    osvi.dwMajorVersion      = 10;
    osvi.dwMinorVersion      = 0;
    osvi.dwBuildNumber       = 22000;

    const DWORDLONG condMask = VerSetConditionMask(
        VerSetConditionMask(
            VerSetConditionMask(0,
                VER_MAJORVERSION, VER_GREATER_EQUAL),
            VER_MINORVERSION, VER_GREATER_EQUAL),
        VER_BUILDNUMBER, VER_GREATER_EQUAL);

    return VerifyVersionInfoW(&osvi,
        VER_MAJORVERSION | VER_MINORVERSION | VER_BUILDNUMBER,
        condMask) != FALSE;
}

std::expected<void, VirtualDesktopError>
VirtualDesktopManager::switchNextFallback() noexcept {
    sendKeySequence({VK_LWIN, VK_CONTROL, VK_RIGHT});
    return {};
}

std::expected<void, VirtualDesktopError>
VirtualDesktopManager::switchPrevFallback() noexcept {
    sendKeySequence({VK_LWIN, VK_CONTROL, VK_LEFT});
    return {};
}

std::expected<void, VirtualDesktopError>
VirtualDesktopManager::createNewFallback() noexcept {
    sendKeySequence({VK_LWIN, VK_CONTROL, 'D'});
    return {};
}

std::expected<void, VirtualDesktopError>
VirtualDesktopManager::closeCurrentFallback() noexcept {
    sendKeySequence({VK_LWIN, VK_CONTROL, VK_F4});
    return {};
}

std::expected<void, VirtualDesktopError>
VirtualDesktopManager::execute(VirtualDesktopCommand cmd) noexcept {
    // Windows 10 以下では常にフォールバック
    // Windows 11 でも IVirtualDesktopManager 非公式 API のため
    // フォールバック（SendInput）のみ使用
    if (!isWindows11OrLater()) {
        return std::unexpected(VirtualDesktopError::NotSupported);
    }

    switch (cmd) {
    case VirtualDesktopCommand::Next:
        return switchNextFallback();
    case VirtualDesktopCommand::Prev:
        return switchPrevFallback();
    case VirtualDesktopCommand::CreateNew:
        return createNewFallback();
    case VirtualDesktopCommand::CloseCurrent:
        return closeCurrentFallback();
    }
    return std::unexpected(VirtualDesktopError::ComError);
}

} // namespace imeindicator::services::hotkey
