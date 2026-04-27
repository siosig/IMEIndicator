#pragma once

#include "../models/ProcessPriorityRule.h"

#include <chrono>
#include <optional>

namespace imeindicator::services {

// 監視結果（既存 [RuleRuntimeState.cs] の MonitorResult と等価）
enum class MonitorResult {
    None,
    Success,
    Skipped,
    Failed,
};

// ルールごとの指数バックオフ状態。ProcessPriorityMonitor が保持する。
class RuleRuntimeState {
public:
    using clock = std::chrono::steady_clock;

    RuleRuntimeState(const models::ProcessPriorityRule& rule,
                     int pollingIntervalSeconds);

    int currentExponent() const noexcept { return currentExponent_; }
    int currentIntervalSeconds() const noexcept;
    clock::time_point nextCheckTime() const noexcept { return nextCheckTime_; }
    std::optional<MonitorResult> lastResult() const noexcept { return lastResult_; }

    bool isDue(clock::time_point now) const noexcept { return now >= nextCheckTime_; }

    void updatePollingInterval(int pollingIntervalSeconds) noexcept;

    // 優先度が目標と一致 → バックオフ指数増加（最大 maxBackoffExponent）。
    void backoffIncrease(clock::time_point now) noexcept;

    // 優先度を変更した → バックオフ指数を 0 にリセット。
    void resetAfterChange(clock::time_point now) noexcept;

    // プロセス未起動 or エラー → バックオフ指数を 0 にリセット。
    void resetAfterSkipOrError(clock::time_point now, MonitorResult result) noexcept;

    // 即時チェック対象に戻す。
    void reset() noexcept;

private:
    const models::ProcessPriorityRule& rule_;
    int pollingIntervalSeconds_;
    int currentExponent_{0};
    clock::time_point nextCheckTime_{};
    std::optional<MonitorResult> lastResult_;
};

} // namespace imeindicator::services
