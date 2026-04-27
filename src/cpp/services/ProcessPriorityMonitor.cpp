#include "ProcessPriorityMonitor.h"

#include "ECoreCpuInfo.h"

#include <algorithm>

namespace imeindicator::services {

namespace {

// std::map<ProcessPriorityRule, ...> 用の比較関数。
// processName + targetPriority + maxBackoffExponent + isEnabled + useECoreOnly を辞書順比較。
bool ruleLess(const models::ProcessPriorityRule& a,
              const models::ProcessPriorityRule& b)
{
    if (a.processName != b.processName) return a.processName < b.processName;
    if (a.targetPriority != b.targetPriority)
        return static_cast<int>(a.targetPriority) < static_cast<int>(b.targetPriority);
    if (a.maxBackoffExponent != b.maxBackoffExponent)
        return a.maxBackoffExponent < b.maxBackoffExponent;
    if (a.isEnabled != b.isEnabled) return a.isEnabled < b.isEnabled;
    return a.useECoreOnly < b.useECoreOnly;
}

} // namespace

ProcessPriorityMonitor::ProcessPriorityMonitor(
    std::shared_ptr<IProcessPriorityService> service)
    : service_(std::move(service))
    , runtimeStates_(&ruleLess)
{
}

ProcessPriorityMonitor::~ProcessPriorityMonitor()
{
    stop();
}

size_t ProcessPriorityMonitor::runtimeStatesCount() const noexcept
{
    std::lock_guard lk(mutex_);
    return runtimeStates_.size();
}

int ProcessPriorityMonitor::pollingIntervalSeconds() const noexcept
{
    std::lock_guard lk(mutex_);
    return pollingIntervalSeconds_;
}

void ProcessPriorityMonitor::start(
    const std::vector<models::ProcessPriorityRule>& rules,
    int pollingIntervalSeconds)
{
    stop();

    {
        std::lock_guard lk(mutex_);
        pollingIntervalSeconds_ = std::clamp(pollingIntervalSeconds, 1, 1800);
    }
    syncRuleStates(rules);

    {
        std::lock_guard lk(mutex_);
        startLoopUnlocked();
    }
}

void ProcessPriorityMonitor::stop() noexcept
{
    std::lock_guard lk(mutex_);
    stopLoopUnlocked();
}

void ProcessPriorityMonitor::updateRules(
    const std::vector<models::ProcessPriorityRule>& rules)
{
    syncRuleStates(rules);

    bool needStart = false;
    {
        std::lock_guard lk(mutex_);
        if (!running_.load() &&
            std::any_of(rules.begin(), rules.end(),
                         [](const auto& r) { return r.isValid() && r.isEnabled; })) {
            needStart = true;
        }
    }
    if (needStart) {
        std::lock_guard lk(mutex_);
        startLoopUnlocked();
    }
}

void ProcessPriorityMonitor::updatePollingInterval(int pollingIntervalSeconds)
{
    const int newInterval = std::clamp(pollingIntervalSeconds, 1, 1800);
    bool restart = false;
    {
        std::lock_guard lk(mutex_);
        if (newInterval == pollingIntervalSeconds_) return;
        pollingIntervalSeconds_ = newInterval;
        for (auto& kv : runtimeStates_) {
            kv.second->updatePollingInterval(newInterval);
        }
        restart = running_.load();
        if (restart) stopLoopUnlocked();
    }
    if (restart) {
        std::lock_guard lk(mutex_);
        startLoopUnlocked();
    }
}

void ProcessPriorityMonitor::syncRuleStates(
    const std::vector<models::ProcessPriorityRule>& rules)
{
    std::lock_guard lk(mutex_);

    // 既存にないルールは削除
    for (auto it = runtimeStates_.begin(); it != runtimeStates_.end(); ) {
        const bool found = std::any_of(rules.begin(), rules.end(),
            [&it](const auto& r) { return !ruleLess(it->first, r) && !ruleLess(r, it->first); });
        if (!found) it = runtimeStates_.erase(it);
        else        ++it;
    }
    // 新規ルールは追加
    for (const auto& r : rules) {
        if (runtimeStates_.find(r) == runtimeStates_.end()) {
            runtimeStates_.emplace(r, std::make_unique<RuleRuntimeState>(
                                          r, pollingIntervalSeconds_));
        }
    }
}

void ProcessPriorityMonitor::startLoopUnlocked()
{
    if (running_.load()) return;
    if (!stopEvent_) {
        stopEvent_ = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!stopEvent_) return;
    } else {
        ::ResetEvent(stopEvent_);
    }
    running_.store(true);
    loopThread_ = std::thread([this]() { loop(); });
}

void ProcessPriorityMonitor::stopLoopUnlocked() noexcept
{
    if (!running_.exchange(false)) return;
    if (stopEvent_) ::SetEvent(stopEvent_);
    if (loopThread_.joinable()) {
        // unlock してから join しないと自己デッドロックの可能性がある。
        // ただし呼び出し側が常に mutex_ 保持中なので、一旦 unlock する。
        mutex_.unlock();
        loopThread_.join();
        mutex_.lock();
    }
    if (stopEvent_) {
        ::CloseHandle(stopEvent_);
        stopEvent_ = nullptr;
    }
}

void ProcessPriorityMonitor::loop()
{
    while (running_.load()) {
        int intervalMs = 0;
        {
            std::lock_guard lk(mutex_);
            intervalMs = pollingIntervalSeconds_ * 1000;
        }
        DWORD rc = ::WaitForSingleObject(stopEvent_, intervalMs);
        if (rc == WAIT_OBJECT_0) break;
        if (!running_.load()) break;

        // スナップショットを取って評価（map 操作中の deadlock 回避）。
        std::vector<std::pair<models::ProcessPriorityRule, RuleRuntimeState*>> snapshot;
        {
            std::lock_guard lk(mutex_);
            snapshot.reserve(runtimeStates_.size());
            for (auto& kv : runtimeStates_) {
                snapshot.emplace_back(kv.first, kv.second.get());
            }
        }

        const auto now = RuleRuntimeState::clock::now();
        for (auto& [rule, state] : snapshot) {
            if (!running_.load()) break;
            if (!state->isDue(now)) continue;
            try {
                evaluateRule(rule, *state);
            } catch (...) {
                state->resetAfterSkipOrError(now, MonitorResult::Failed);
            }
        }
    }
}

void ProcessPriorityMonitor::evaluateRule(const models::ProcessPriorityRule& rule,
                                           RuleRuntimeState& state)
{
    const auto now = RuleRuntimeState::clock::now();

    if (!rule.isValid() || !rule.isEnabled) {
        state.resetAfterSkipOrError(now, MonitorResult::Skipped);
        return;
    }

    const std::wstring processName = rule.normalizedProcessName();
    const DWORD targetClass = models::priorityLevelToProcessPriorityClass(rule.targetPriority);

    std::vector<ProcessPriorityEntry> processes;
    try {
        processes = service_->getProcessPriorities(processName);
    } catch (...) {
        state.resetAfterSkipOrError(now, MonitorResult::Failed);
        return;
    }
    if (processes.empty()) {
        state.resetAfterSkipOrError(now, MonitorResult::Skipped);
        return;
    }

    bool anyChanged = false, anyFailed = false;
    const bool applyAffinity = rule.useECoreOnly &&
                                ECoreCpuInfo::instance().hasECores();
    const DWORD_PTR eCoreMask = applyAffinity
                                  ? ECoreCpuInfo::instance().eCoreMask()
                                  : 0;

    for (const auto& p : processes) {
        if (p.currentPriorityClass != targetClass) {
            if (service_->setPriority(p.processId, targetClass)) anyChanged = true;
            else anyFailed = true;
        }
        if (applyAffinity) {
            auto cur = service_->getAffinity(p.processId);
            if (!cur.has_value()) {
                anyFailed = true;
            } else if (*cur != eCoreMask) {
                if (service_->setAffinity(p.processId, eCoreMask)) anyChanged = true;
                else anyFailed = true;
            }
        }
    }

    if (anyFailed)         state.resetAfterSkipOrError(now, MonitorResult::Failed);
    else if (anyChanged)   state.resetAfterChange(now);
    else                   state.backoffIncrease(now);
}

} // namespace imeindicator::services
