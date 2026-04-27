#include "services/ColorHelper.h"

#include <gtest/gtest.h>

using namespace imeindicator;

namespace {

void expectChannelNear(float actual, float expected)
{
    // 整数 RGB を float に正規化した値の比較は誤差 1/255 以内であれば一致とみなす。
    EXPECT_NEAR(actual, expected, 1.0f / 255.0f / 2.0f);
}

void expectColor(const D2D1_COLOR_F& c, float r, float g, float b, float a)
{
    expectChannelNear(c.r, r);
    expectChannelNear(c.g, g);
    expectChannelNear(c.b, b);
    expectChannelNear(c.a, a);
}

} // namespace

// ============================================================
// parseColor — #RRGGBB 形式
// ============================================================

TEST(ColorHelperTests, ParseColor_RRGGBB_Red)
{
    expectColor(services::ColorHelper::parseColor("#FF0000"), 1.0f, 0.0f, 0.0f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_RRGGBB_Green)
{
    expectColor(services::ColorHelper::parseColor("#00FF00"), 0.0f, 1.0f, 0.0f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_RRGGBB_Blue)
{
    expectColor(services::ColorHelper::parseColor("#0000FF"), 0.0f, 0.0f, 1.0f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_RRGGBB_White)
{
    expectColor(services::ColorHelper::parseColor("#FFFFFF"), 1.0f, 1.0f, 1.0f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_RRGGBB_Black)
{
    expectColor(services::ColorHelper::parseColor("#000000"), 0.0f, 0.0f, 0.0f, 1.0f);
}

// ============================================================
// parseColor — #RGB 形式
// ============================================================

TEST(ColorHelperTests, ParseColor_RGB_Red)
{
    expectColor(services::ColorHelper::parseColor("#F00"), 1.0f, 0.0f, 0.0f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_RGB_Green)
{
    expectColor(services::ColorHelper::parseColor("#0F0"), 0.0f, 1.0f, 0.0f, 1.0f);
}

// ============================================================
// parseColor — #AARRGGBB 形式
// ============================================================

TEST(ColorHelperTests, ParseColor_AARRGGBB_SemiTransparentRed)
{
    auto c = services::ColorHelper::parseColor("#80FF0000");
    expectChannelNear(c.a, 0x80 / 255.0f);
    expectChannelNear(c.r, 1.0f);
    expectChannelNear(c.g, 0.0f);
    expectChannelNear(c.b, 0.0f);
}

TEST(ColorHelperTests, ParseColor_AARRGGBB_OpaqueWhite)
{
    auto c = services::ColorHelper::parseColor("#FFFFFFFF");
    expectColor(c, 1.0f, 1.0f, 1.0f, 1.0f);
}

// ============================================================
// parseColor — 不正入力 → 灰色
// ============================================================

TEST(ColorHelperTests, ParseColor_Empty_ReturnsGray)
{
    expectColor(services::ColorHelper::parseColor(""), 0.5f, 0.5f, 0.5f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_WhitespaceOnly_ReturnsGray)
{
    expectColor(services::ColorHelper::parseColor("   "), 0.5f, 0.5f, 0.5f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_InvalidChars_ReturnsGray)
{
    expectColor(services::ColorHelper::parseColor("#ZZZZZZ"), 0.5f, 0.5f, 0.5f, 1.0f);
}
TEST(ColorHelperTests, ParseColor_InvalidLength_ReturnsGray)
{
    expectColor(services::ColorHelper::parseColor("#12345"), 0.5f, 0.5f, 0.5f, 1.0f);
}

// ============================================================
// toRgbHex / toArgbHex
// ============================================================

TEST(ColorHelperTests, ToRgbHex_Black)
{
    EXPECT_EQ(services::ColorHelper::toRgbHex({0.0f, 0.0f, 0.0f, 1.0f}), "#000000");
}
TEST(ColorHelperTests, ToRgbHex_White)
{
    EXPECT_EQ(services::ColorHelper::toRgbHex({1.0f, 1.0f, 1.0f, 1.0f}), "#FFFFFF");
}
TEST(ColorHelperTests, ToRgbHex_RoundTrip)
{
    auto orig = services::ColorHelper::parseColor("#3B82F6");
    auto hex = services::ColorHelper::toRgbHex(orig);
    auto parsed = services::ColorHelper::parseColor(hex);
    EXPECT_EQ(hex, "#3B82F6");
    expectChannelNear(orig.r, parsed.r);
    expectChannelNear(orig.g, parsed.g);
    expectChannelNear(orig.b, parsed.b);
}
TEST(ColorHelperTests, ToArgbHex_RoundTrip)
{
    auto orig = services::ColorHelper::parseColor("#80FF0000");
    auto hex = services::ColorHelper::toArgbHex(orig);
    auto parsed = services::ColorHelper::parseColor(hex);
    EXPECT_EQ(hex, "#80FF0000");
    expectChannelNear(orig.a, parsed.a);
    expectChannelNear(orig.r, parsed.r);
    expectChannelNear(orig.g, parsed.g);
    expectChannelNear(orig.b, parsed.b);
}

// ============================================================
// isValidHexColor
// ============================================================

TEST(ColorHelperTests, IsValidHexColor_Valid)
{
    EXPECT_TRUE(services::ColorHelper::isValidHexColor("#FF0000"));
    EXPECT_TRUE(services::ColorHelper::isValidHexColor("#F00"));
    EXPECT_TRUE(services::ColorHelper::isValidHexColor("#80FF0000"));
    EXPECT_TRUE(services::ColorHelper::isValidHexColor("#F00F"));
    EXPECT_TRUE(services::ColorHelper::isValidHexColor("FF0000"));  // # 無しでも許容
}
TEST(ColorHelperTests, IsValidHexColor_Invalid)
{
    EXPECT_FALSE(services::ColorHelper::isValidHexColor("#ZZZZZZ"));
    EXPECT_FALSE(services::ColorHelper::isValidHexColor("#12345"));
    EXPECT_FALSE(services::ColorHelper::isValidHexColor(""));
    EXPECT_FALSE(services::ColorHelper::isValidHexColor("   "));
}
