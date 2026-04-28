#include "TrayIcon.h"

#include "../app/AppConstants.h"
#include "../win32/NativeConstants.h"

#include <vector>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <shellapi.h>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "user32.lib")

namespace imeindicator::views {

namespace {

constexpr wchar_t kClassName[] = L"IMEIndicator_TrayMessageWindow";
constexpr UINT kTrayIconId = 1;

} // namespace

TrayIcon::TrayIcon() = default;

TrayIcon::~TrayIcon()
{
    if (iconInstalled_) {
        ::Shell_NotifyIconW(NIM_DELETE, &iconData_);
        iconInstalled_ = false;
    }
    if (messageHwnd_) {
        ::DestroyWindow(messageHwnd_);
        messageHwnd_ = nullptr;
    }
    if (windowClass_ && hInstance_) {
        ::UnregisterClassW(kClassName, hInstance_);
        windowClass_ = 0;
    }
}

bool TrayIcon::initialize(HINSTANCE hInstance)
{
    hInstance_ = hInstance;

    // 受信専用ウィンドウを作る
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = &TrayIcon::wndProcStatic;
    wc.hInstance = hInstance;
    wc.lpszClassName = kClassName;
    windowClass_ = ::RegisterClassExW(&wc);
    if (!windowClass_) return false;

    messageHwnd_ = ::CreateWindowExW(
        0, kClassName, L"", 0, 0, 0, 0, 0,
        HWND_MESSAGE, nullptr, hInstance, this);
    if (!messageHwnd_) return false;

    // app.rc 埋め込みのアイコンをロード（IDI_APPLICATION = 32512 ではなく自前ID にすると
    // app-debug.ico/app.ico が使い分けられる。ここでは単純に LoadIconW(hInstance, IDI_APPLICATION)。）
    HICON hIcon = ::LoadIconW(hInstance, IDI_APPLICATION);
    if (!hIcon) hIcon = ::LoadIconW(nullptr, IDI_APPLICATION);

    iconData_ = {};
    iconData_.cbSize = sizeof(iconData_);
    iconData_.hWnd = messageHwnd_;
    iconData_.uID = kTrayIconId;
    iconData_.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    iconData_.uCallbackMessage = imeindicator::win32::WM_APP_TRAY_NOTIFY;
    iconData_.hIcon = hIcon;
    ::lstrcpynW(iconData_.szTip, L"IME Indicator",
                ARRAYSIZE(iconData_.szTip));

    if (!::Shell_NotifyIconW(NIM_ADD, &iconData_)) return false;
    iconInstalled_ = true;

    // バージョン 4 に切り替え（モダンな WM_CONTEXTMENU を有効に）
    iconData_.uVersion = NOTIFYICON_VERSION_4;
    ::Shell_NotifyIconW(NIM_SETVERSION, &iconData_);

    return true;
}

void TrayIcon::showBalloon(const std::wstring& title, const std::wstring& body) noexcept
{
    if (!iconInstalled_) return;
    NOTIFYICONDATAW d{};
    d.cbSize = sizeof(d);
    d.hWnd = messageHwnd_;
    d.uID = kTrayIconId;
    d.uFlags = NIF_INFO;
    ::lstrcpynW(d.szInfoTitle, title.c_str(), ARRAYSIZE(d.szInfoTitle));
    ::lstrcpynW(d.szInfo, body.c_str(), ARRAYSIZE(d.szInfo));
    d.dwInfoFlags = NIIF_INFO;
    ::Shell_NotifyIconW(NIM_MODIFY, &d);
}

LRESULT CALLBACK TrayIcon::wndProcStatic(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        ::SetWindowLongPtrW(hwnd, GWLP_USERDATA,
                            reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
        return ::DefWindowProcW(hwnd, msg, wp, lp);
    }
    auto* self = reinterpret_cast<TrayIcon*>(
        ::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (self) return self->handleMessage(msg, wp, lp);
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT TrayIcon::handleMessage(UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == imeindicator::win32::WM_APP_TRAY_NOTIFY) {
        // NOTIFYICON_VERSION_4 では LPARAM の下位ワードがマウスイベント
        UINT mouseEvent = LOWORD(lp);
        onTrayNotify(mouseEvent);
        return 0;
    }
    if (msg == WM_COMMAND) {
        const int id = LOWORD(wp);
        switch (id) {
            case IDM_TOGGLE_VISIBLE:
                if (toggleVisibleCb_ && getIsVisibleCb_) {
                    toggleVisibleCb_(!getIsVisibleCb_());
                }
                return 0;
            case IDM_POWER_BEST_POWER_EFFICIENCY:
                if (setPowerModeCb_) setPowerModeCb_(models::PowerMode::BestPowerEfficiency);
                return 0;
            case IDM_POWER_BALANCED:
                if (setPowerModeCb_) setPowerModeCb_(models::PowerMode::Balanced);
                return 0;
            case IDM_POWER_BEST_PERFORMANCE:
                if (setPowerModeCb_) setPowerModeCb_(models::PowerMode::BestPerformance);
                return 0;
            case IDM_OPEN_SETTINGS:
                if (openSettingsCb_) openSettingsCb_();
                return 0;
            case IDM_EXIT:
                if (exitCb_) exitCb_();
                else ::PostQuitMessage(0);
                return 0;
            default:
                // Phase 5 / US3: ホットキーサブメニュー（5000〜5255）
                if (id >= IDM_HOTKEY_BASE && id <= IDM_HOTKEY_MAX) {
                    if (executeHotkeyCb_) {
                        executeHotkeyCb_(id - IDM_HOTKEY_BASE);
                    }
                    return 0;
                }
                break;
        }
    }
    return ::DefWindowProcW(messageHwnd_, msg, wp, lp);
}

void TrayIcon::onTrayNotify(UINT mouseEvent)
{
    switch (mouseEvent) {
        case WM_LBUTTONUP:
        case WM_LBUTTONDBLCLK:
            if (openSettingsCb_) openSettingsCb_();
            break;
        case WM_RBUTTONUP:
        case WM_CONTEXTMENU:
            showContextMenu();
            break;
        default:
            break;
    }
}

HMENU TrayIcon::buildContextMenu()
{
    HMENU menu = ::CreatePopupMenu();
    if (!menu) return nullptr;

    // 表示切替（チェック可能）
    UINT toggleFlags = MF_STRING;
    if (getIsVisibleCb_ && getIsVisibleCb_()) toggleFlags |= MF_CHECKED;
    ::AppendMenuW(menu, toggleFlags, IDM_TOGGLE_VISIBLE, L"表示切替");
    ::AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);

    // 電源モードサブメニュー
    HMENU powerMenu = ::CreatePopupMenu();
    if (powerMenu) {
        const models::PowerMode current = getCurrentPowerModeCb_
                                              ? getCurrentPowerModeCb_()
                                              : models::PowerMode::Balanced;
        auto append = [&](models::PowerMode mode, int id, const wchar_t* label) {
            UINT flags = MF_STRING;
            if (current == mode) flags |= MF_CHECKED;
            ::AppendMenuW(powerMenu, flags, static_cast<UINT_PTR>(id), label);
        };
        append(models::PowerMode::BestPowerEfficiency,
               IDM_POWER_BEST_POWER_EFFICIENCY, L"最適な電力効率");
        append(models::PowerMode::Balanced,
               IDM_POWER_BALANCED, L"バランス");
        append(models::PowerMode::BestPerformance,
               IDM_POWER_BEST_PERFORMANCE, L"最適なパフォーマンス");
        ::AppendMenuW(menu, MF_POPUP | MF_STRING,
                      reinterpret_cast<UINT_PTR>(powerMenu), L"電源モード");
    }

    // Phase 5 / US3: ホットキーサブメニュー（trayMenu=true のエントリのみ）
    if (getTrayHotkeysCb_) {
        const auto hotkeys = getTrayHotkeysCb_();
        // trayMenu=true のエントリ + そのインデックスを集計
        struct TrayItem { int index; const models::hotkey::HotKeyEntry* entry; };
        std::vector<TrayItem> items;
        items.reserve(16);
        int idx = 0;
        for (const auto& hk : hotkeys) {
            if (hk.trayMenu && !hk.disable) {
                items.push_back({idx, &hk});
            }
            ++idx;
        }
        if (!items.empty()) {
            ::AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
            HMENU hkMenu = ::CreatePopupMenu();
            if (hkMenu) {
                for (const auto& it : items) {
                    if (it.index < 0 || it.index > (IDM_HOTKEY_MAX - IDM_HOTKEY_BASE)) continue;
                    const auto& dn = it.entry->displayName();
                    ::AppendMenuW(hkMenu, MF_STRING,
                                  static_cast<UINT_PTR>(IDM_HOTKEY_BASE + it.index),
                                  dn.empty() ? L"(無題)" : dn.c_str());
                }
                ::AppendMenuW(menu, MF_POPUP | MF_STRING,
                              reinterpret_cast<UINT_PTR>(hkMenu), L"ホットキー");
            }
        }
    }

    ::AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    ::AppendMenuW(menu, MF_STRING, IDM_OPEN_SETTINGS, L"設定");
    ::AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    ::AppendMenuW(menu, MF_STRING, IDM_EXIT, L"終了");

    return menu;
}

void TrayIcon::showContextMenu()
{
    HMENU menu = buildContextMenu();
    if (!menu) return;

    POINT pt{};
    ::GetCursorPos(&pt);

    // メニューがアクティブウィンドウ外で適切に閉じるよう SetForegroundWindow が必要。
    ::SetForegroundWindow(messageHwnd_);
    ::TrackPopupMenuEx(menu,
                       TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN,
                       pt.x, pt.y, messageHwnd_, nullptr);
    ::PostMessageW(messageHwnd_, WM_NULL, 0, 0);
    ::DestroyMenu(menu);
}

} // namespace imeindicator::views
