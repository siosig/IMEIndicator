#include "services/DisplayHelper.h"

#include <gtest/gtest.h>

#include <algorithm>

using namespace imeindicator;

namespace {

bool hasScreens()
{
    return services::DisplayHelper::getScreenCount() > 0;
}

} // namespace

// 既存 [DisplayHelperTests.cs] と等価シナリオ。
// CI/開発機ともディスプレイ 0 件は想定しないが、保険として GTEST_SKIP で逃がす。

TEST(DisplayHelperTests, GetDisplayIndexFromPosition_PrimaryCenter_ReturnsZero)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    auto primary = services::DisplayHelper::getScreenBounds(0);
    const double cx = primary.left + primary.width / 2.0;
    const double cy = primary.top + primary.height / 2.0;

    const int idx = services::DisplayHelper::getDisplayIndexFromPosition(
        cx - 50, cy - 50, 100, 100);
    EXPECT_EQ(idx, 0);
}

TEST(DisplayHelperTests, IsValidDisplayIndex_Zero_ReturnsTrue)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";
    EXPECT_TRUE(services::DisplayHelper::isValidDisplayIndex(0));
}

TEST(DisplayHelperTests, IsValidDisplayIndex_Negative_ReturnsFalse)
{
    EXPECT_FALSE(services::DisplayHelper::isValidDisplayIndex(-1));
}

TEST(DisplayHelperTests, IsValidDisplayIndex_TooLarge_ReturnsFalse)
{
    EXPECT_FALSE(services::DisplayHelper::isValidDisplayIndex(9999));
}

TEST(DisplayHelperTests, IsPositionOnAnyDisplay_PrimaryCenter_ReturnsTrue)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    auto primary = services::DisplayHelper::getScreenBounds(0);
    const double cx = primary.left + primary.width / 2.0;
    const double cy = primary.top + primary.height / 2.0;
    EXPECT_TRUE(services::DisplayHelper::isPositionOnAnyDisplay(cx, cy));
}

TEST(DisplayHelperTests, IsPositionOnAnyDisplay_FarOffScreen_ReturnsFalse)
{
    EXPECT_FALSE(services::DisplayHelper::isPositionOnAnyDisplay(-99999.0, -99999.0));
}

TEST(DisplayHelperTests, GetValidPosition_ValidCoordinates_ReturnsOriginal)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    auto primary = services::DisplayHelper::getScreenBounds(0);
    const double x = primary.left + 100;
    const double y = primary.top + 100;

    auto v = services::DisplayHelper::getValidPosition(x, y, 100, 100, 0);
    EXPECT_DOUBLE_EQ(v.x, x);
    EXPECT_DOUBLE_EQ(v.y, y);
    EXPECT_GE(v.displayIndex, 0);
}

TEST(DisplayHelperTests, GetValidPosition_InvalidCoordinates_ReturnsFallback)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    auto v = services::DisplayHelper::getValidPosition(-99999.0, -99999.0, 100, 100, 0);
    // フォールバック位置は何らかのディスプレイ内にある。
    EXPECT_TRUE(services::DisplayHelper::isPositionOnAnyDisplay(v.x + 50, v.y + 50));
    EXPECT_EQ(v.displayIndex, 0);
}

TEST(DisplayHelperTests, GetValidPosition_InvalidDisplayIndex_FallsBackToZero)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    auto v = services::DisplayHelper::getValidPosition(-99999.0, -99999.0, 100, 100, 9999);
    EXPECT_EQ(v.displayIndex, 0);
}

TEST(DisplayHelperTests, GetAllMonitors_HasNonZeroSizes)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    auto monitors = services::DisplayHelper::getAllMonitors();
    ASSERT_FALSE(monitors.empty());
    for (const auto& m : monitors) {
        EXPECT_GT(m.width(), 0);
        EXPECT_GT(m.height(), 0);
        EXPECT_GE(m.dpiX, 96u);
        EXPECT_GE(m.dpiY, 96u);
    }
}

// 013-ime-corner-image: 背景画像ウィンドウの配置先モニター取得（research.md R-5）。

TEST(DisplayHelperTests, GetPrimaryMonitor_HasScreens_ReturnsValueWithValidGeometry)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    const auto primary = services::DisplayHelper::getPrimaryMonitor();
    ASSERT_TRUE(primary.has_value());

    // ワーク領域（タスクバー除外）は空でない。
    const auto& wr = primary->workRect;
    EXPECT_GT(wr.right - wr.left, 0);
    EXPECT_GT(wr.bottom - wr.top, 0);

    // GetDpiForMonitor 失敗時も 96 にフォールバックするため、96 未満にはならない。
    EXPECT_GE(primary->dpiX, 96u);
    EXPECT_GE(primary->dpiY, 96u);
}

TEST(DisplayHelperTests, GetPrimaryMonitor_WorkRect_MatchesPrimaryWorkArea)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    const auto primary = services::DisplayHelper::getPrimaryMonitor();
    ASSERT_TRUE(primary.has_value());

    // 既存 getPrimaryWorkArea() と同じ探索規則（プライマリ → 先頭）であること。
    const auto wa = services::DisplayHelper::getPrimaryWorkArea();
    EXPECT_EQ(primary->workRect.left,   wa.left);
    EXPECT_EQ(primary->workRect.top,    wa.top);
    EXPECT_EQ(primary->workRect.right,  wa.right);
    EXPECT_EQ(primary->workRect.bottom, wa.bottom);
}

TEST(DisplayHelperTests, GetPrimaryMonitor_PrimaryFlagged_ReturnsPrimaryEntry)
{
    if (!hasScreens()) GTEST_SKIP() << "no display";

    // MONITORINFOF_PRIMARY を持つモニターが列挙に含まれるなら、それが返る。
    const auto monitors = services::DisplayHelper::getAllMonitors();
    const bool anyPrimary = std::any_of(monitors.begin(), monitors.end(),
                                        [](const auto& m) { return m.isPrimary; });
    if (!anyPrimary) GTEST_SKIP() << "no monitor flagged as primary";

    const auto primary = services::DisplayHelper::getPrimaryMonitor();
    ASSERT_TRUE(primary.has_value());
    EXPECT_TRUE(primary->isPrimary);
}
