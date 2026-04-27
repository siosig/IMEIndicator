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
    static constexpr std::wstring_view BrokenSuffixPrefix = L".broken-";
    static constexpr std::wstring_view TempSuffix = L".tmp";

    // 制限値
    static constexpr int MaxProcessPriorityRules = 30;
    static constexpr int MinPollingIntervalSeconds = 1;
    static constexpr int MaxPollingIntervalSeconds = 1800;
    static constexpr int MaxIndicatorTextChars = 8;
    static constexpr int MaxBackoffExponentLimit = 10;

    // ロガーカテゴリ（contracts/log-file-contract.md §ロガー）
    static constexpr std::string_view LoggerApp = "app";
    static constexpr std::string_view LoggerIme = "ime";
    static constexpr std::string_view LoggerPixel = "pixel";
    static constexpr std::string_view LoggerPriority = "priority";
    static constexpr std::string_view LoggerPower = "power";
    static constexpr std::string_view LoggerDisplay = "display";
    static constexpr std::string_view LoggerTray = "tray";
    static constexpr std::string_view LoggerSettings = "settings";
};

} // namespace imeindicator::app
