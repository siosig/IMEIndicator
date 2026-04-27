#include "services/ECoreCpuInfo.h"

#include <gtest/gtest.h>

using namespace imeindicator;

// ホストの CPU 構成に依存するため、E-Core 有無に応じた最低限のチェックのみ。
// 既存 [ECoreCpuInfoTests.cs] と同じスキップ条件。
TEST(ECoreCpuInfoTests, SingletonReturnsSameInstance)
{
    const auto& a = services::ECoreCpuInfo::instance();
    const auto& b = services::ECoreCpuInfo::instance();
    EXPECT_EQ(&a, &b);
}

TEST(ECoreCpuInfoTests, NonHybridCpuHasZeroMask)
{
    const auto& info = services::ECoreCpuInfo::instance();
    if (!info.hasECores()) {
        EXPECT_EQ(info.eCoreMask(), 0u);
        EXPECT_EQ(info.eCoreCount(), 0);
    }
}

TEST(ECoreCpuInfoTests, HybridCpuExposesNonZeroMask)
{
    const auto& info = services::ECoreCpuInfo::instance();
    if (info.hasECores()) {
        EXPECT_NE(info.eCoreMask(), 0u);
        EXPECT_GT(info.eCoreCount(), 0);
        EXPECT_GT(info.pCoreCount(), 0);
    }
}
