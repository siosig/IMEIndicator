#pragma once

#include "../models/ProcessPriorityRule.h"
#include "IProcessPriorityService.h"
#include "RuleRuntimeState.h"

#include <atomic>
#include <map>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// プロセス優先度・アフィニティの定期監視エンジン。
// 既存 [ProcessPriorityMonitor.cs] と等価動作（指数バックオフ + システム共通ポーリング間隔）。
//
// E-Core 限定アフィニティが指定されたルールでは、ECoreCpuInfo から取得した
// E-Core マスクを SetAffinity で適用する。
class ProcessPriorityMonitor {
public:
    explicit ProcessPriorityMonitor(std::shared_ptr<IProcessPriorityService> service);
    ~ProcessPriorityMonitor();

    ProcessPriorityMonitor(const ProcessPriorityMonitor&) = delete;
    ProcessPriorityMonitor& operator=(const ProcessPriorityMonitor&) = delete;

    void start(const std::vector<models::ProcessPriorityRule>& rules,
               int pollingIntervalSeconds);
    void stop() noexcept;

    void updateRules(const std::vector<models::ProcessPriorityRule>& rules);
    void updatePollingInterval(int pollingIntervalSeconds);

    // 単一ルールの評価（テスト公開用）。RuleRuntimeState は外部で生成・所有する。
    void evaluateRule(const models::ProcessPriorityRule& rule,
                      RuleRuntimeState& state);

    // 現在管理中の RuleRuntimeState 数（テスト用）
    size_t runtimeStatesCount() const noexcept;

    int pollingIntervalSeconds() const noexcept;

private:
    void syncRuleStates(const std::vector<models::ProcessPriorityRule>& rules);
    void startLoopUnlocked();
    void stopLoopUnlocked() noexcept;
    void loop();

    std::shared_ptr<IProcessPriorityService> service_;
    mutable std::mutex mutex_;
    std::map<models::ProcessPriorityRule, std::unique_ptr<RuleRuntimeState>,
             bool(*)(const models::ProcessPriorityRule&,
                     const models::ProcessPriorityRule&)>
        runtimeStates_;
    int pollingIntervalSeconds_{1};
    std::atomic<bool> running_{false};
    HANDLE stopEvent_{nullptr};
    std::thread loopThread_;
};

} // namespace imeindicator::services
