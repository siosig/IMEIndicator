#include "models/ProcessPriorityRule.h"

#include <gtest/gtest.h>

using namespace imeindicator::models;

TEST(ProcessPriorityRuleTests, NormalizedNameStripsExeAndTrim)
{
    ProcessPriorityRule r;
    r.processName = L"  chrome.exe ";
    EXPECT_EQ(r.normalizedProcessName(), L"chrome");

    r.processName = L"NOTEPAD.EXE";
    EXPECT_EQ(r.normalizedProcessName(), L"NOTEPAD");

    r.processName = L"foo";
    EXPECT_EQ(r.normalizedProcessName(), L"foo");
}

TEST(ProcessPriorityRuleTests, IsValidRejectsBlankNames)
{
    ProcessPriorityRule r;
    r.processName = L"";   EXPECT_FALSE(r.isValid());
    r.processName = L"   "; EXPECT_FALSE(r.isValid());
    r.processName = L"x"; EXPECT_TRUE(r.isValid());
}

TEST(ProcessPriorityRuleTests, ValidatedMaxBackoffExponentClamps)
{
    ProcessPriorityRule r;
    r.maxBackoffExponent = -5; EXPECT_EQ(r.validatedMaxBackoffExponent(), 0);
    r.maxBackoffExponent = 99; EXPECT_EQ(r.validatedMaxBackoffExponent(), 10);
    r.maxBackoffExponent = 5;  EXPECT_EQ(r.validatedMaxBackoffExponent(), 5);
}

TEST(ProcessPriorityRuleTests, PriorityLevelStringRoundTrip)
{
    for (auto lv : { PriorityLevel::Idle, PriorityLevel::BelowNormal,
                     PriorityLevel::Normal, PriorityLevel::AboveNormal,
                     PriorityLevel::High, PriorityLevel::Realtime }) {
        const char* s = priorityLevelToString(lv);
        PriorityLevel parsed{};
        ASSERT_TRUE(tryParsePriorityLevel(s, parsed)) << s;
        EXPECT_EQ(parsed, lv);
    }
}

TEST(ProcessPriorityRuleTests, JsonRoundTrip)
{
    ProcessPriorityRule r;
    r.processName = L"chrome.exe";
    r.targetPriority = PriorityLevel::BelowNormal;
    r.maxBackoffExponent = 4;
    r.isEnabled = true;
    r.useECoreOnly = true;

    nlohmann::json j;
    to_json(j, r);
    EXPECT_EQ(j["targetPriority"], "BelowNormal");
    EXPECT_EQ(j["useECoreOnly"], true);

    ProcessPriorityRule r2;
    from_json(j, r2);
    EXPECT_EQ(r2, r);
}

TEST(ProcessPriorityRuleTests, PriorityClassMapping)
{
    EXPECT_EQ(priorityLevelToProcessPriorityClass(PriorityLevel::Idle),
              static_cast<DWORD>(IDLE_PRIORITY_CLASS));
    EXPECT_EQ(priorityLevelToProcessPriorityClass(PriorityLevel::BelowNormal),
              static_cast<DWORD>(BELOW_NORMAL_PRIORITY_CLASS));
    EXPECT_EQ(priorityLevelToProcessPriorityClass(PriorityLevel::Normal),
              static_cast<DWORD>(NORMAL_PRIORITY_CLASS));
    EXPECT_EQ(priorityLevelToProcessPriorityClass(PriorityLevel::AboveNormal),
              static_cast<DWORD>(ABOVE_NORMAL_PRIORITY_CLASS));
    EXPECT_EQ(priorityLevelToProcessPriorityClass(PriorityLevel::High),
              static_cast<DWORD>(HIGH_PRIORITY_CLASS));
    EXPECT_EQ(priorityLevelToProcessPriorityClass(PriorityLevel::Realtime),
              static_cast<DWORD>(REALTIME_PRIORITY_CLASS));
}
