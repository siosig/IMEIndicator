/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - Accessibility 実装
 IAccessible を使ってシステムトレイアイコンの情報を取得。
 spy.exe の主要機能を EXE 内に統合。
*/

#include "Accessibility.h"
#include <oleacc.h>
#include <wil/com.h>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

#pragma comment(lib, "Oleacc.lib")

namespace {

struct AccessibilityErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override {
        return "AccessibilityError";
    }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<AccessibilityError>(ev)) {
        case AccessibilityError::WindowNotFound: return "Tray window not found";
        case AccessibilityError::ComError:       return "IAccessible COM error";
        case AccessibilityError::NoIcons:        return "No tray icons found";
        }
        return "Unknown AccessibilityError";
    }
};

const AccessibilityErrorCategory& accessibilityErrorCategory() noexcept {
    static AccessibilityErrorCategory cat;
    return cat;
}

// ウィンドウハンドルから IAccessible を取得
[[nodiscard]] std::expected<wil::com_ptr<IAccessible>, AccessibilityError>
getAccessible(HWND hwnd) {
    wil::com_ptr<IAccessible> acc;
    const HRESULT hr = AccessibleObjectFromWindow(
        hwnd, OBJID_WINDOW, IID_PPV_ARGS(acc.put()));
    if (FAILED(hr)) return std::unexpected(AccessibilityError::ComError);
    return acc;
}

// 子ウィンドウを再帰的に検索（クラス名で一致）
[[nodiscard]] HWND findDescendant(HWND parent, const wchar_t* className) noexcept {
    HWND child = FindWindowExW(parent, nullptr, className, nullptr);
    if (child) return child;
    // 直接の子で見つからない場合は再帰検索
    child = GetWindow(parent, GW_CHILD);
    while (child) {
        HWND found = findDescendant(child, className);
        if (found) return found;
        child = GetWindow(child, GW_HWNDNEXT);
    }
    return nullptr;
}

} // namespace

std::error_code make_error_code(AccessibilityError e) {
    return {static_cast<int>(e), accessibilityErrorCategory()};
}

std::expected<std::vector<TrayIconInfo>, AccessibilityError>
enumerateTrayIcons() {
    // Shell_TrayWnd → TrayNotifyWnd → SysPager → ToolbarWindow32
    HWND trayWnd = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!trayWnd) return std::unexpected(AccessibilityError::WindowNotFound);

    HWND notifyWnd = FindWindowExW(trayWnd, nullptr, L"TrayNotifyWnd", nullptr);
    if (!notifyWnd) return std::unexpected(AccessibilityError::WindowNotFound);

    HWND sysPageWnd = FindWindowExW(notifyWnd, nullptr, L"SysPager", nullptr);
    if (!sysPageWnd) {
        // Windows 11 では SysPager が ToolbarWindow32 の親になっていない場合あり
        sysPageWnd = notifyWnd;
    }

    HWND toolbarWnd = FindWindowExW(sysPageWnd, nullptr,
                                    L"ToolbarWindow32", nullptr);
    if (!toolbarWnd) return std::unexpected(AccessibilityError::WindowNotFound);

    // IAccessible を取得
    auto accResult = getAccessible(toolbarWnd);
    if (!accResult) return std::unexpected(accResult.error());

    auto& acc = accResult.value();

    // 子要素数を取得
    LONG childCount = 0;
    if (FAILED(acc->get_accChildCount(&childCount)) || childCount <= 0) {
        return std::unexpected(AccessibilityError::NoIcons);
    }

    std::vector<VARIANT> children(static_cast<size_t>(childCount));
    LONG obtained = 0;
    if (FAILED(AccessibleChildren(acc.get(), 0, childCount,
                                   children.data(), &obtained))) {
        return std::unexpected(AccessibilityError::ComError);
    }

    std::vector<TrayIconInfo> icons;
    icons.reserve(static_cast<size_t>(obtained));

    for (LONG i = 0; i < obtained; ++i) {
        if (children[i].vt != VT_DISPATCH && children[i].vt != VT_I4) {
            continue;
        }

        // アイコン名を取得
        BSTR bstrName = nullptr;
        if (SUCCEEDED(acc->get_accName(children[i], &bstrName)) && bstrName) {
            TrayIconInfo info;
            info.name = bstrName;
            SysFreeString(bstrName);

            // 位置を取得
            LONG x = 0, y = 0, w = 0, h = 0;
            if (SUCCEEDED(acc->accLocation(&x, &y, &w, &h, children[i]))) {
                info.x      = static_cast<int>(x);
                info.y      = static_cast<int>(y);
                info.width  = static_cast<int>(w);
                info.height = static_cast<int>(h);
            }

            if (!info.name.empty()) {
                icons.push_back(std::move(info));
            }
        }

        if (children[i].vt == VT_DISPATCH && children[i].pdispVal) {
            children[i].pdispVal->Release();
        }
    }

    return icons;
}

std::expected<void, AccessibilityError>
clickTrayIcon(const TrayIconInfo& icon) noexcept {
    if (icon.width <= 0 || icon.height <= 0) {
        return std::unexpected(AccessibilityError::NoIcons);
    }

    // アイコン中心をクリック
    const int cx = icon.x + icon.width  / 2;
    const int cy = icon.y + icon.height / 2;

    const int screenW = GetSystemMetrics(SM_CXSCREEN);
    const int screenH = GetSystemMetrics(SM_CYSCREEN);
    if (screenW <= 0 || screenH <= 0) {
        return std::unexpected(AccessibilityError::ComError);
    }

    const LONG normX = static_cast<LONG>(cx * 65535 / screenW);
    const LONG normY = static_cast<LONG>(cy * 65535 / screenH);

    INPUT inputs[3]{};
    inputs[0].type       = INPUT_MOUSE;
    inputs[0].mi.dx      = normX;
    inputs[0].mi.dy      = normY;
    inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;
    inputs[1].type       = INPUT_MOUSE;
    inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
    inputs[2].type       = INPUT_MOUSE;
    inputs[2].mi.dwFlags = MOUSEEVENTF_LEFTUP;

    SendInput(3, inputs, sizeof(INPUT));
    return {};
}

} // namespace imeindicator::services::hotkey
