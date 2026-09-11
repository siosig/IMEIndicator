#pragma once

#include "../models/PowerMode.h"
#include "../models/hotkey/HotKeyEntry.h"

#include <functional>
#include <span>
#include <string>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <shellapi.h>

namespace imeindicator::views {

// タスクトレイアイコンと右クリックメニュー（contracts/tray-ui-contract.md 準拠）。
// メッセージ受信用の独自ウィンドウを内部で持ち、Shell_NotifyIconW のコールバック
// `WM_APP_TRAY_NOTIFY` を受け取って左クリック/右クリックを処理する。
class TrayIcon {
public:
    // メニュー項目クリックのコールバック。値はクリックされたメニュー ID。
    using MenuActionCallback = std::function<void(int menuId)>;
    // 設定ウィンドウを開く要求（左クリック）
    using OpenSettingsCallback = std::function<void()>;
    // アプリ終了要求（右クリック → 終了）
    using ExitCallback = std::function<void()>;
    // 表示切替トグル（チェック前の現在値を引数で受ける）
    using ToggleVisibleCallback = std::function<void(bool newVisible)>;
    // 電源モード変更（クリックされたモード）
    using SetPowerModeCallback = std::function<void(models::PowerMode mode)>;
    // 電源モードのチェック表示用（メニュー表示直前に呼ばれる）
    using GetCurrentPowerModeCallback = std::function<models::PowerMode()>;
    // 表示切替のチェック表示用
    using GetIsVisibleCallback = std::function<bool()>;
    // 背景画像表示切替（チェック前の現在値の反転を引数で受ける）
    using ToggleBackgroundImageCallback = std::function<void(bool newVisible)>;
    // 背景画像表示のチェック表示用
    using GetIsBackgroundImageVisibleCallback = std::function<bool()>;

    // メニュー項目 ID（WM_COMMAND の wParam）
    enum MenuId : int {
        IDM_TOGGLE_VISIBLE              = 1001,
        // 013-ime-corner-image。1002〜1004 は 010 契約の予約済み ID のため 1005
        IDM_TOGGLE_BACKGROUND_IMAGE     = 1005,
        IDM_POWER_BEST_POWER_EFFICIENCY = 1010,
        IDM_POWER_BALANCED              = 1011,
        IDM_POWER_BEST_PERFORMANCE      = 1012,
        IDM_OPEN_SETTINGS               = 1020,
        IDM_EXIT                        = 1099,
        // Phase 5 / US3: ホットキーサブメニュー（5000〜5255）
        IDM_HOTKEY_BASE                 = 5000,
        IDM_HOTKEY_MAX                  = 5255,
    };

    // Phase 5 / US3: ホットキーエントリのコールバック
    // メニュー表示時に trayMenu=true のエントリを取得
    using GetTrayHotkeysCallback =
        std::function<std::span<const models::hotkey::HotKeyEntry>()>;
    // メニュー項目クリックでホットキー実行（インデックスは hotkeys() 配列内の位置）
    using ExecuteHotkeyCallback = std::function<void(int hotkeyIndex)>;

    TrayIcon();
    ~TrayIcon();

    TrayIcon(const TrayIcon&) = delete;
    TrayIcon& operator=(const TrayIcon&) = delete;

    bool initialize(HINSTANCE hInstance);

    // バルーン通知（/powertoggle 受信時のみに使用）
    void showBalloon(const std::wstring& title, const std::wstring& body) noexcept;

    // 各種コールバック登録
    void setOpenSettingsCallback(OpenSettingsCallback cb) { openSettingsCb_ = std::move(cb); }
    void setExitCallback(ExitCallback cb) { exitCb_ = std::move(cb); }
    void setToggleVisibleCallback(ToggleVisibleCallback cb) { toggleVisibleCb_ = std::move(cb); }
    void setSetPowerModeCallback(SetPowerModeCallback cb) { setPowerModeCb_ = std::move(cb); }
    void setGetCurrentPowerModeCallback(GetCurrentPowerModeCallback cb) { getCurrentPowerModeCb_ = std::move(cb); }
    void setGetIsVisibleCallback(GetIsVisibleCallback cb) { getIsVisibleCb_ = std::move(cb); }
    // 013-ime-corner-image: 背景画像表示切替（カーソル追従インジケーターとは独立）
    void setToggleBackgroundImageCallback(ToggleBackgroundImageCallback cb) { toggleBackgroundImageCb_ = std::move(cb); }
    void setGetIsBackgroundImageVisibleCallback(GetIsBackgroundImageVisibleCallback cb) { getIsBackgroundImageVisibleCb_ = std::move(cb); }

    // Phase 5 / US3: ホットキーサブメニュー用コールバック
    void setGetTrayHotkeysCallback(GetTrayHotkeysCallback cb) { getTrayHotkeysCb_ = std::move(cb); }
    void setExecuteHotkeyCallback(ExecuteHotkeyCallback cb) { executeHotkeyCb_ = std::move(cb); }

    HWND messageHwnd() const noexcept { return messageHwnd_; }

private:
    static LRESULT CALLBACK wndProcStatic(HWND, UINT, WPARAM, LPARAM);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    void onTrayNotify(UINT mouseEvent);
    void showContextMenu();
    HMENU buildContextMenu();

    // === 状態 ===
    HINSTANCE hInstance_{nullptr};
    HWND messageHwnd_{nullptr};
    ATOM windowClass_{0};
    NOTIFYICONDATAW iconData_{};
    bool iconInstalled_{false};

    OpenSettingsCallback openSettingsCb_;
    ExitCallback exitCb_;
    ToggleVisibleCallback toggleVisibleCb_;
    SetPowerModeCallback setPowerModeCb_;
    GetCurrentPowerModeCallback getCurrentPowerModeCb_;
    GetIsVisibleCallback getIsVisibleCb_;
    // 013-ime-corner-image
    ToggleBackgroundImageCallback toggleBackgroundImageCb_;
    GetIsBackgroundImageVisibleCallback getIsBackgroundImageVisibleCb_;
    // Phase 5 / US3
    GetTrayHotkeysCallback getTrayHotkeysCb_;
    ExecuteHotkeyCallback executeHotkeyCb_;
};

} // namespace imeindicator::views
