#pragma once

#include "../models/AppSettings.h"
#include "../models/LanguageInfo.h"
#include "../services/SettingsManager.h"

#include <atomic>
#include <filesystem>
#include <memory>
#include <thread>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {
class IMEMonitor;
class ProcessPriorityService;
class ProcessPriorityMonitor;
}
namespace imeindicator::services::hotkey {
class HotkeyService;
}
namespace imeindicator::views {
class MouseCursorIndicatorWindow;
class TrayIcon;
class SettingsDialog;
class BackgroundImageWindow;
}

namespace imeindicator::app {

// アプリ全体のライフサイクル統括。
// IMEMonitor / MouseCursorIndicatorWindow / TrayIcon / PowerModeBackup と
// /powertoggle IPC を統合する（既存 [App.xaml.cs] 相当）。
class App {
public:
    App();
    ~App();

    App(const App&) = delete;
    App& operator=(const App&) = delete;

    bool initialize(HINSTANCE hInstance);
    void shutdown();
    int  runMessageLoop();

    HWND messageHwnd() const noexcept { return messageHwnd_; }
    services::SettingsManager& settingsManager() noexcept { return settingsManager_; }
    std::filesystem::path localAppDataDir() const noexcept { return localAppDataDir_; }

    // /powertoggle 経由で電源モードをトグル + バルーン通知（UI スレッドから呼ぶ）
    void togglePowerModeAndNotify();

    // 表示切替（トレイメニュー）
    void setMouseIndicatorVisible(bool visible);

    // 013-ime-corner-image: 背景画像表示の有効/無効を設定・永続化し、現在の IME 状態で表示反映する（トレイ・ホットキーからも使う単一経路）
    void setBackgroundImageVisible(bool visible);
    void toggleBackgroundImageVisible();

private:
    bool createMessageWindow(HINSTANCE hInstance);
    static LRESULT CALLBACK messageWndProc(HWND, UINT, WPARAM, LPARAM);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    void onIMEStateChanged(const models::LanguageInfo& info);  // ワーカスレッドから呼ばれる → PostMessage でマーシャリング
    void onCursorPositionChanged(int x, int y);
    void applyWindowVisibility(const models::LanguageInfo& info);
    void applyBackgroundImageVisibility(bool imeOn);
    void refreshIndicatorColor();

    void startPowerToggleListener();
    void stopPowerToggleListener();

    // FR-011: 電源モードを設定する直前にバックアップを書き出す。
    void backupCurrentPowerMode();
    // 正常終了時にバックアップファイルを削除（次回起動で復元発火を防ぐ）。
    void clearPowerModeBackup();

    // 内部状態
    HINSTANCE hInstance_{nullptr};
    HWND messageHwnd_{nullptr};
    ATOM messageWndClass_{0};
    std::filesystem::path localAppDataDir_;

    services::SettingsManager settingsManager_;
    std::unique_ptr<services::IMEMonitor> imeMonitor_;
    std::unique_ptr<views::MouseCursorIndicatorWindow> indicatorWindow_;
    std::unique_ptr<views::BackgroundImageWindow> backgroundImageWindow_;
    std::unique_ptr<views::TrayIcon> trayIcon_;
    std::shared_ptr<services::ProcessPriorityService> priorityService_;
    std::unique_ptr<services::ProcessPriorityMonitor> priorityMonitor_;
    std::unique_ptr<views::SettingsDialog> settingsDialog_;

    // HotkeyP マージ (010-hotkeyp-merge): グローバルホットキー機能のオーケストレーション。
    // initialize() で start、shutdown() で stop、設定変更時に reload を呼ぶ。
    std::unique_ptr<services::hotkey::HotkeyService> hotkeyService_;

    // 直近の IME 状態（メッセージ経由で UI スレッドへ渡す保管領域）
    std::atomic<int> latestLanguage_{0};
    std::atomic<bool> latestImeOn_{false};

    // /powertoggle IPC
    HANDLE powerToggleEvent_{nullptr};
    HANDLE powerToggleStopEvent_{nullptr};
    std::thread powerToggleThread_;
    std::atomic<bool> powerToggleStop_{false};
};

} // namespace imeindicator::app
