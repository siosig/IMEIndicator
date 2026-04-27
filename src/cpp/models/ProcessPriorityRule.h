#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <string>
#include <string_view>

#include <nlohmann/json.hpp>

namespace imeindicator::models {

// Windows プロセス優先度（タスクマネージャー準拠の 6 段階）
// 既存 C# 版 ProcessPriorityRule.PriorityLevel と完全一致
enum class PriorityLevel : int {
    Idle        = 0,
    BelowNormal = 1,
    Normal      = 2,
    AboveNormal = 3,
    High        = 4,
    Realtime    = 5
};

// PriorityLevel ↔ JSON 文字列（"Idle"/"BelowNormal"/...）
const char* priorityLevelToString(PriorityLevel level) noexcept;
bool tryParsePriorityLevel(std::string_view s, PriorityLevel& out) noexcept;

// PriorityLevel → Win32 PRIORITY_CLASS 定数
DWORD priorityLevelToProcessPriorityClass(PriorityLevel level) noexcept;

// プロセス優先度制御ルール（data-model.md §3）
struct ProcessPriorityRule {
    std::wstring processName;             // .exe 拡張子は許容（内部で除去）
    PriorityLevel targetPriority{PriorityLevel::Normal};
    int    maxBackoffExponent{6};
    bool   isEnabled{true};
    bool   useECoreOnly{false};

    // 末尾 ".exe"（大小無視）と両端空白を除去
    std::wstring normalizedProcessName() const;

    // バリデーション: 空白トリム後に非空
    bool isValid() const;

    // バリデーション後の MaxBackoffExponent（[0, 10] でクランプ）
    int validatedMaxBackoffExponent() const;

    bool operator==(const ProcessPriorityRule&) const = default;
};

// nlohmann::json アダプタ
void to_json(nlohmann::json& j, const ProcessPriorityRule& r);
void from_json(const nlohmann::json& j, ProcessPriorityRule& r);

} // namespace imeindicator::models
