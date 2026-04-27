#pragma once

#include "../models/AppSettings.h"
#include "../services/SettingsManager.h"

#include <filesystem>
#include <memory>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::app {

// アプリ全体のライフサイクル統括（旧 App.xaml.cs 相当）。
// Phase 2 ではコンストラクション・初期化・終了処理だけを定義し、
// IMEMonitor / MouseCursorIndicatorWindow / TrayIcon / ProcessPriorityMonitor 等の
// 具体的サービスは Phase 3〜5 で composeServices() に追加される。
class App {
public:
    App();
    ~App();

    App(const App&) = delete;
    App& operator=(const App&) = delete;

    // 初期化: 設定読み込み・ロガー初期化・サービス構築
    bool initialize(HINSTANCE hInstance);

    // 終了処理: サービス停止・PowerModeBackup クリーンアップ・ロガー shutdown
    void shutdown();

    // メッセージループ。`/powertoggle` の二重起動側からは呼ばれない。
    int runMessageLoop();

    // メインウィンドウ HWND（メッセージ受信専用、非表示）
    HWND messageHwnd() const noexcept { return messageHwnd_; }

    // 設定マネージャ取得（設定ダイアログから利用）
    services::SettingsManager& settingsManager() noexcept { return settingsManager_; }

    // ローカル AppData ディレクトリ（ログ・状態用）
    std::filesystem::path localAppDataDir() const noexcept { return localAppDataDir_; }

private:
    // メッセージウィンドウ（非表示）を作成し、トレイ通知などをここで受ける
    bool createMessageWindow(HINSTANCE hInstance);

    static LRESULT CALLBACK messageWndProc(HWND, UINT, WPARAM, LPARAM);

    HINSTANCE hInstance_{nullptr};
    HWND messageHwnd_{nullptr};
    ATOM messageWndClass_{0};
    std::filesystem::path localAppDataDir_;

    services::SettingsManager settingsManager_;
};

} // namespace imeindicator::app
