#include "RuleRuntimeState.h"

#include <algorithm>

namespace imeindicator::services {

RuleRuntimeState::RuleRuntimeState(const models::ProcessPriorityRule& rule,
                                   int pollingIntervalSeconds)
    : rule_(rule)
    , pollingIntervalSeconds_(std::max(1, pollingIntervalSeconds))
{
    reset();
}

int RuleRuntimeState::currentIntervalSeconds() const noexcept
{
    return pollingIntervalSeconds_ * (1 << currentExponent_);
}

void RuleRuntimeState::updatePollingInterval(int pollingIntervalSeconds) noexcept
{
    pollingIntervalSeconds_ = std::max(1, pollingIntervalSeconds);
}

void RuleRuntimeState::backoffIncrease(clock::time_point now) noexcept
{
    lastResult_ = MonitorResult::Skipped;
    const int maxExp = std::clamp(rule_.maxBackoffExponent, 0, 10);
    currentExponent_ = std::min(currentExponent_ + 1, maxExp);
    nextCheckTime_ = now + std::chrono::seconds(currentIntervalSeconds());
}

void RuleRuntimeState::resetAfterChange(clock::time_point now) noexcept
{
    lastResult_ = MonitorResult::Success;
    currentExponent_ = 0;
    nextCheckTime_ = now + std::chrono::seconds(currentIntervalSeconds());
}

void RuleRuntimeState::resetAfterSkipOrError(clock::time_point now,
                                              MonitorResult result) noexcept
{
    lastResult_ = result;
    currentExponent_ = 0;
    nextCheckTime_ = now + std::chrono::seconds(currentIntervalSeconds());
}

void RuleRuntimeState::reset() noexcept
{
    currentExponent_ = 0;
    lastResult_.reset();
    nextCheckTime_ = clock::now();
}

} // namespace imeindicator::services
