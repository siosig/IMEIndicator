#include "services/RuleRuntimeState.h"
#include "models/ProcessPriorityRule.h"

#include <gtest/gtest.h>

using namespace imeindicator;

namespace {

models::ProcessPriorityRule makeRule(int maxBackoff = 6)
{
    models::ProcessPriorityRule r;
    r.processName = L"chrome";
    r.targetPriority = models::PriorityLevel::BelowNormal;
    r.maxBackoffExponent = maxBackoff;
    r.isEnabled = true;
    r.useECoreOnly = false;
    return r;
}

} // namespace

TEST(RuleRuntimeStateTests, InitiallyDueAndExponentZero)
{
    auto rule = makeRule();
    services::RuleRuntimeState s(rule, 1);
    EXPECT_EQ(s.currentExponent(), 0);
    EXPECT_TRUE(s.isDue(services::RuleRuntimeState::clock::now()));
    EXPECT_FALSE(s.lastResult().has_value());
}

TEST(RuleRuntimeStateTests, BackoffIncreaseUpToMax)
{
    auto rule = makeRule(3);
    services::RuleRuntimeState s(rule, 1);
    auto now = services::RuleRuntimeState::clock::now();
    for (int i = 0; i < 10; ++i) s.backoffIncrease(now);
    EXPECT_EQ(s.currentExponent(), 3);
    EXPECT_EQ(s.lastResult().value(), services::MonitorResult::Skipped);
}

TEST(RuleRuntimeStateTests, ResetAfterChangeReturnsToZero)
{
    auto rule = makeRule(3);
    services::RuleRuntimeState s(rule, 1);
    auto now = services::RuleRuntimeState::clock::now();
    s.backoffIncrease(now);
    s.backoffIncrease(now);
    s.resetAfterChange(now);
    EXPECT_EQ(s.currentExponent(), 0);
    EXPECT_EQ(s.lastResult().value(), services::MonitorResult::Success);
}

TEST(RuleRuntimeStateTests, ResetAfterFailedKeepsResult)
{
    auto rule = makeRule(6);
    services::RuleRuntimeState s(rule, 1);
    auto now = services::RuleRuntimeState::clock::now();
    s.resetAfterSkipOrError(now, services::MonitorResult::Failed);
    EXPECT_EQ(s.currentExponent(), 0);
    EXPECT_EQ(s.lastResult().value(), services::MonitorResult::Failed);
}

TEST(RuleRuntimeStateTests, IntervalDoublesWithExponent)
{
    auto rule = makeRule(6);
    services::RuleRuntimeState s(rule, 1);
    auto now = services::RuleRuntimeState::clock::now();
    EXPECT_EQ(s.currentIntervalSeconds(), 1);
    s.backoffIncrease(now);
    EXPECT_EQ(s.currentIntervalSeconds(), 2);
    s.backoffIncrease(now);
    EXPECT_EQ(s.currentIntervalSeconds(), 4);
}

TEST(RuleRuntimeStateTests, UpdatePollingIntervalChangesEffectiveInterval)
{
    auto rule = makeRule(6);
    services::RuleRuntimeState s(rule, 1);
    EXPECT_EQ(s.currentIntervalSeconds(), 1);
    s.updatePollingInterval(5);
    EXPECT_EQ(s.currentIntervalSeconds(), 5);
}
