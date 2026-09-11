#pragma once

#include <string_view>

namespace imeindicator::app {

// アプリケーション全体で共有する定数。
// 名前付きカーネルオブジェクト（Mutex/Event）は contracts/ipc-contract.md の規定に従う。
struct AppConstants {
    // 表示名・トレイチップ等
    static constexpr std::wstring_view AppName = L"IMEIndicator";
    static constexpr std::wstring_view DisplayName = L"IME Indicator";

    // シングルインスタンス Mutex（contracts/ipc-contract.md §1）
    static constexpr std::wstring_view MutexName = L"IMEIndicator_SingleInstance";

    // /powertoggle IPC Event（contracts/ipc-contract.md §2）
    static constexpr std::wstring_view PowerToggleEventName = L"IMEIndicator_PowerToggle";

    // 設定ファイル名（Release / Debug）
#ifdef _DEBUG
    static constexpr std::wstring_view SettingsFileName = L"settings-d.json";
#else
    static constexpr std::wstring_view SettingsFileName = L"settings.json";
#endif

    // ログ・状態ファイル
    static constexpr std::wstring_view LogFileName = L"imeindicator.log";
    static constexpr std::wstring_view PowerBackupFileName = L"power-mode-backup.json";

    // ディレクトリ
    static constexpr std::wstring_view LogsSubDir = L"logs";
    static constexpr std::wstring_view StateSubDir = L"state";
    static constexpr std::wstring_view DebugSubDir = L"debug";

    // バックアップ・破損ファイルのサフィックス
    static constexpr std::wstring_view V1BackupSuffix = L".v1.bak";
    static constexpr std::wstring_view V2BackupSuffix = L".v2.bak";
    // v3 → v4 昇格時のバックアップ（013-ime-corner-image / contracts/settings-schema-v4.md）
    static constexpr std::wstring_view V3BackupSuffix = L".v3.bak";
    static constexpr std::wstring_view BrokenSuffixPrefix = L".broken-";
    static constexpr std::wstring_view TempSuffix = L".tmp";

    // 制限値
    static constexpr int MaxProcessPriorityRules = 30;
    static constexpr int MinPollingIntervalSeconds = 1;
    static constexpr int MaxPollingIntervalSeconds = 1800;
    static constexpr int MaxIndicatorTextChars = 8;
    static constexpr int MaxBackoffExponentLimit = 10;

    // 画面右上 IME ON 背景画像（013-ime-corner-image / spec FR-012）
    // 論理ピクセル。表示先モニターの DPI で拡縮する（BackgroundImageLayout::compute）。
    static constexpr int BackgroundImageLogicalSize   = 128;
    static constexpr int BackgroundImageLogicalMargin = 16;

    // ロガーカテゴリ（contracts/log-file-contract.md §ロガー）
    static constexpr std::string_view LoggerApp = "app";
    static constexpr std::string_view LoggerIme = "ime";
    static constexpr std::string_view LoggerPixel = "pixel";
    static constexpr std::string_view LoggerPriority = "priority";
    static constexpr std::string_view LoggerPower = "power";
    static constexpr std::string_view LoggerDisplay = "display";
    static constexpr std::string_view LoggerTray = "tray";
    static constexpr std::string_view LoggerSettings = "settings";

    // ロガーカテゴリ（HotkeyP マージ追加 / 010-hotkeyp-merge）
    static constexpr std::string_view LoggerHotkey  = "hotkey";   // HotkeyService 全般
    static constexpr std::string_view LoggerHook    = "hook";     // HookEngine
    static constexpr std::string_view LoggerCommand = "command";  // CommandExecutor / commands/
    static constexpr std::string_view LoggerMacro   = "macro";    // マクロ実行

    // トレイメニュー項目 ID 範囲（contracts/hotkey-tray-menu-contract.md）
    static constexpr int TrayToggleIndicator    = 1001;
    static constexpr int TrayTogglePixel        = 1002;
    static constexpr int TrayOpenSettings       = 1003;
    static constexpr int TrayExit               = 1004;
    static constexpr int TrayOpenHotkeySettings = 1010;  // 新規（ホットキー設定タブを開く）
    // 013-ime-corner-image: 背景画像表示切替（1002〜1004 は上記の予約済み ID のため 1005）
    static constexpr int TrayToggleBackgroundImage = 1005;
    static constexpr int TrayHotkeyBase         = 5000;  // 5000 + N で N 番目のホットキーを実行
    static constexpr int TrayHotkeyMax          = 5255;  // 256 件上限（FR-001）

    // Win32 メッセージ ID（HookEngine から PostMessage で配信）
    // WM_HOTKEY_RAW_KBD/WM_HOTKEY_RAW_MOUSE は services/hotkey/HookEngine.h で定義（重複定義回避）
};

} // namespace imeindicator::app
