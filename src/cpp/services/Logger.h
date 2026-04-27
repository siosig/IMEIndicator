#pragma once

#include "../models/AppSettings.h"

#include <filesystem>
#include <memory>

#include <spdlog/spdlog.h>

namespace imeindicator::services {

// ログ初期化を統括するシングルトン的サービス。
// contracts/log-file-contract.md に従い、ローテーションファイルシンクとカテゴリ別ロガーを構築する。
class Logger {
public:
    // 初期化。複数回呼び出した場合は 2 回目以降は no-op（パスが異なる場合のみ警告）。
    // logsDirectory は %LOCALAPPDATA%\IMEIndicator\logs。テスト時は注入可能。
    static void init(const std::filesystem::path& logsDirectory,
                     models::LogLevel initialLevel,
                     bool enableMsvcSinkInDebug = true);

    // ログレベルを全カテゴリに即時反映する（contracts/log-file-contract.md §ログレベル）
    static void setGlobalLevel(models::LogLevel level);

    // spdlog::shutdown() を呼んでログをフラッシュする。アプリ終了時に呼び出す。
    static void shutdown();

    // models::LogLevel ↔ spdlog::level::level_enum 変換
    static spdlog::level::level_enum toSpdlogLevel(models::LogLevel level) noexcept;

    // 既に初期化済みかどうか
    static bool isInitialized() noexcept;

private:
    Logger() = delete;
};

} // namespace imeindicator::services
