#include "services/ProcessPriorityMonitor.h"
#include "services/IProcessPriorityService.h"
#include "models/ProcessPriorityRule.h"

#include <gtest/gtest.h>

#include <atomic>
#include <memory>
#include <unordered_map>

using namespace imeindicator;

namespace {

// 単純なフェイク実装（モック）。GoogleMock は別途リンク不要。
class FakePriorityService : public services::IProcessPriorityService {
public:
    std::vector<services::ProcessPriorityEntry>
        getProcessPriorities(const std::wstring& name) override
    {
        getCalls.fetch_add(1);
        auto it = mockProcesses.find(name);
        if (it == mockProcesses.end()) return {};
        return it->second;
    }
    bool setPriority(DWORD pid, DWORD pc) override
    {
        setCalls.fetch_add(1);
        if (failSetPriority) return false;
        for (auto& kv : mockProcesses) {
            for (auto& e : kv.second) {
                if (e.processId == pid) e.currentPriorityClass = pc;
            }
        }
        return true;
    }
    std::optional<DWORD_PTR> getAffinity(DWORD /*pid*/) override
    {
        return DWORD_PTR{1};
    }
    bool setAffinity(DWORD /*pid*/, DWORD_PTR) override { return true; }

    std::unordered_map<std::wstring, std::vector<services::ProcessPriorityEntry>> mockProcesses;
    std::atomic<int> getCalls{0};
    std::atomic<int> setCalls{0};
    bool failSetPriority{false};
};

models::ProcessPriorityRule makeRule(const wchar_t* name, models::PriorityLevel pri)
{
    models::ProcessPriorityRule r;
    r.processName = name;
    r.targetPriority = pri;
    r.maxBackoffExponent = 6;
    r.isEnabled = true;
    r.useECoreOnly = false;
    return r;
}

} // namespace

TEST(ProcessPriorityMonitorTests, EvaluateRuleSkippedWhenNoProcess)
{
    auto svc = std::make_shared<FakePriorityService>();
    services::ProcessPriorityMonitor mon(svc);
    auto rule = makeRule(L"chrome", models::PriorityLevel::BelowNormal);
    services::RuleRuntimeState state(rule, 1);
    mon.evaluateRule(rule, state);
    EXPECT_EQ(state.lastResult().value(), services::MonitorResult::Skipped);
}

TEST(ProcessPriorityMonitorTests, EvaluateRuleChangesPriority)
{
    auto svc = std::make_shared<FakePriorityService>();
    svc->mockProcesses[L"chrome"] = {{1234, NORMAL_PRIORITY_CLASS}};
    services::ProcessPriorityMonitor mon(svc);
    auto rule = makeRule(L"chrome", models::PriorityLevel::BelowNormal);
    services::RuleRuntimeState state(rule, 1);
    mon.evaluateRule(rule, state);
    EXPECT_EQ(state.lastResult().value(), services::MonitorResult::Success);
    EXPECT_GE(svc->setCalls.load(), 1);
}

TEST(ProcessPriorityMonitorTests, EvaluateRuleSkippedWhenAlreadyTarget)
{
    auto svc = std::make_shared<FakePriorityService>();
    svc->mockProcesses[L"chrome"] = {{1234, BELOW_NORMAL_PRIORITY_CLASS}};
    services::ProcessPriorityMonitor mon(svc);
    auto rule = makeRule(L"chrome", models::PriorityLevel::BelowNormal);
    services::RuleRuntimeState state(rule, 1);
    mon.evaluateRule(rule, state);
    EXPECT_EQ(state.lastResult().value(), services::MonitorResult::Skipped);
}

TEST(ProcessPriorityMonitorTests, EvaluateRuleFailedWhenSetFails)
{
    auto svc = std::make_shared<FakePriorityService>();
    svc->mockProcesses[L"chrome"] = {{1234, NORMAL_PRIORITY_CLASS}};
    svc->failSetPriority = true;
    services::ProcessPriorityMonitor mon(svc);
    auto rule = makeRule(L"chrome", models::PriorityLevel::BelowNormal);
    services::RuleRuntimeState state(rule, 1);
    mon.evaluateRule(rule, state);
    EXPECT_EQ(state.lastResult().value(), services::MonitorResult::Failed);
}

TEST(ProcessPriorityMonitorTests, UpdateRulesSyncsRuntimeStates)
{
    auto svc = std::make_shared<FakePriorityService>();
    services::ProcessPriorityMonitor mon(svc);
    std::vector<models::ProcessPriorityRule> rules = {
        makeRule(L"chrome", models::PriorityLevel::BelowNormal),
        makeRule(L"opera", models::PriorityLevel::Normal),
    };
    mon.updateRules(rules);
    EXPECT_EQ(mon.runtimeStatesCount(), 2u);
    rules.pop_back();
    mon.updateRules(rules);
    EXPECT_EQ(mon.runtimeStatesCount(), 1u);
    mon.stop();
}
