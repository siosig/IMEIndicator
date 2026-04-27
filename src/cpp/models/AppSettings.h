#pragma once

#include "MouseCursorIndicatorSettings.h"
#include "ProcessPriorityRule.h"

#include <string>
#include <vector>

#include <nlohmann/json.hpp>

namespace imeindicator::models {

// ロガーレベル列挙（contracts/settings-schema-v2.md / log-file-contract.md §ログレベル）
enum class LogLevel : int {
    Trace    = 0,
    Debug    = 1,
    Info     = 2,
    Warn     = 3,
    Error    = 4,
    Critical = 5
};

const char* logLevelToString(LogLevel level) noexcept;
bool tryParseLogLevel(std::string_view s, LogLevel& out) noexcept;  // 大小無視

// ルートエンティティ AppSettings（data-model.md §1）
// settings-schema-v1.md / v2.md に厳密に従う。
struct AppSettings {
    // schemaVersion: 未指定→1（v1 として扱い、書き出し時に 2 へ昇格）。
    // 永続化済みの値は from_json で復元。
    int schemaVersion{2};

    MouseCursorIndicatorSettings mouseCursorIndicator{};

    std::wstring imeOnText{L"あ"};
    std::wstring imeOffText{L"A"};

    bool isFirstLaunch{true};

    std::vector<ProcessPriorityRule> processPriorityRules{};

    int pollingIntervalSeconds{1};

    // v2 新規
    LogLevel logLevel{LogLevel::Warn};
    int pixelVerificationIntervalMs{2000};

    // 値域 Clamp（data-model.md §バリデーションサマリ）
    void clamp();

    bool operator==(const AppSettings&) const = default;
};

// nlohmann::json アダプタ（camelCase 維持。未知フィールド無視。デフォルト値補完）
void to_json(nlohmann::json& j, const AppSettings& s);
void from_json(const nlohmann::json& j, AppSettings& s);

} // namespace imeindicator::models
