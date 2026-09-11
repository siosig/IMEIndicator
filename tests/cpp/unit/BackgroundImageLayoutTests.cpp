// 013-ime-corner-image / spec FR-004, FR-011, FR-012
// BackgroundImageLayout::compute の単体テスト。
// data-model.md §3 の期待値表（7 ケース）+ dpi=0 に加え、短辺選択・既定引数の上書きを検証する。
// 純関数（Win32 呼び出しは MulDiv のみ）なので、ディスプレイ構成に依存せず常に実行できる。
#include "services/BackgroundImageLayout.h"

#include <gtest/gtest.h>

using namespace imeindicator;

namespace {

using Layout = services::BackgroundImageLayout;

RECT makeRect(LONG left, LONG top, LONG right, LONG bottom) noexcept
{
    return RECT{left, top, right, bottom};
}

// 4 辺を一度に比較し、失敗時は期待値と実際の矩形をまとめて表示する。
::testing::AssertionResult rectEquals(const RECT& actual,
                                      LONG left, LONG top, LONG right, LONG bottom)
{
    if (actual.left == left && actual.top == top &&
        actual.right == right && actual.bottom == bottom) {
        return ::testing::AssertionSuccess();
    }
    return ::testing::AssertionFailure()
        << "expected (" << left << "," << top << "," << right << "," << bottom << ")"
        << " but got (" << actual.left << "," << actual.top << ","
        << actual.right << "," << actual.bottom << ")";
}

// 矩形が作業領域に収まっているか（FR-004: 作業領域の右上に配置、タスクバーと重ならない）。
::testing::AssertionResult rectWithin(const RECT& inner, const RECT& outer)
{
    if (inner.left >= outer.left && inner.top >= outer.top &&
        inner.right <= outer.right && inner.bottom <= outer.bottom) {
        return ::testing::AssertionSuccess();
    }
    return ::testing::AssertionFailure()
        << "rect (" << inner.left << "," << inner.top << "," << inner.right << "," << inner.bottom
        << ") is not within (" << outer.left << "," << outer.top << ","
        << outer.right << "," << outer.bottom << ")";
}

} // namespace

// ---- 定数（FR-012: 128 論理 px 四方、余白 16 論理 px）----

TEST(BackgroundImageLayoutTests, Constants_MatchSpec)
{
    EXPECT_EQ(Layout::kLogicalSize, 128);
    EXPECT_EQ(Layout::kLogicalMargin, 16);
}

// ---- data-model.md §3 期待値表 ----

TEST(BackgroundImageLayoutTests, Compute_FullHd100Percent_TopRightWithMargin)
{
    const RECT work = makeRect(0, 0, 1920, 1080);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, 1776, 16, 1904, 144));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_FullHd150Percent_ScalesSizeAndMargin)
{
    // size = 128 * 144 / 96 = 192, margin = 16 * 144 / 96 = 24
    const RECT rc = Layout::compute(makeRect(0, 0, 1920, 1080), 144);
    EXPECT_TRUE(rectEquals(rc, 1704, 24, 1896, 216));
}

TEST(BackgroundImageLayoutTests, Compute_4K200Percent_ScalesSizeAndMargin)
{
    // size = 256, margin = 32
    const RECT rc = Layout::compute(makeRect(0, 0, 3840, 2160), 192);
    EXPECT_TRUE(rectEquals(rc, 3552, 32, 3808, 288));
}

TEST(BackgroundImageLayoutTests, Compute_TaskbarTop_OffsetsTopByWorkAreaTop)
{
    // タスクバー上端: 作業領域の top が 48 → 画像の top は 48 + 16
    const RECT work = makeRect(0, 48, 1920, 1080);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, 1776, 64, 1904, 192));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_TaskbarRight_OffsetsLeftByWorkAreaRight)
{
    // タスクバー右端: 作業領域の right が 1856 → 画像の right は 1856 - 16
    const RECT work = makeRect(0, 0, 1856, 1080);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, 1712, 16, 1840, 144));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_NegativePrimaryCoordinates_UsesVirtualScreenOrigin)
{
    // プライマリがセカンダリの左側にある構成（仮想スクリーン座標が負）
    const RECT work = makeRect(-2560, 0, 0, 1440);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, -144, 16, -16, 144));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_TinyWorkArea_ShrinksToFit)
{
    // avail = min(100, 100) - 2 * 16 = 68 < 128 → size = 68
    const RECT work = makeRect(0, 0, 100, 100);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, 16, 16, 84, 84));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_DpiZero_TreatedAs96)
{
    const RECT work = makeRect(0, 0, 1920, 1080);
    const RECT rc = Layout::compute(work, 0);
    EXPECT_TRUE(rectEquals(rc, 1776, 16, 1904, 144));

    const RECT at96 = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, at96.left, at96.top, at96.right, at96.bottom));
}

// ---- 縮小規則の詳細（avail は短辺基準）----

TEST(BackgroundImageLayoutTests, Compute_WideButShortWorkArea_UsesHeightAsLimit)
{
    // 幅 1920 でも高さ 100 → avail = 100 - 32 = 68
    const RECT work = makeRect(0, 0, 1920, 100);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, 1836, 16, 1904, 84));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_TallButNarrowWorkArea_UsesWidthAsLimit)
{
    // 高さ 1080 でも幅 100 → avail = 100 - 32 = 68
    const RECT work = makeRect(0, 0, 100, 1080);
    const RECT rc = Layout::compute(work, 96);
    EXPECT_TRUE(rectEquals(rc, 16, 16, 84, 84));
    EXPECT_TRUE(rectWithin(rc, work));
}

TEST(BackgroundImageLayoutTests, Compute_WorkAreaSmallerThanMargins_ClampsSizeToOne)
{
    // avail = 20 - 32 = -12 → size = max(1, -12) = 1（規則どおり最小 1 px）
    const RECT rc = Layout::compute(makeRect(0, 0, 20, 20), 96);
    EXPECT_EQ(rc.right - rc.left, 1);
    EXPECT_EQ(rc.bottom - rc.top, 1);
    EXPECT_TRUE(rectEquals(rc, 3, 16, 4, 17));
}

// ---- 既定引数の上書き ----

TEST(BackgroundImageLayoutTests, Compute_CustomLogicalSizeAndMargin_AreHonored)
{
    // 64 論理 px 四方、余白 8 @ 96 dpi → left = 1920 - 8 - 64
    const RECT rc = Layout::compute(makeRect(0, 0, 1920, 1080), 96, 64, 8);
    EXPECT_TRUE(rectEquals(rc, 1848, 8, 1912, 72));
}

TEST(BackgroundImageLayoutTests, Compute_CustomLogicalSizeAndMargin_ScaleWithDpi)
{
    // 64/8 @ 192 dpi は 128/16 @ 96 dpi と同じ物理矩形になる
    const RECT rc = Layout::compute(makeRect(0, 0, 1920, 1080), 192, 64, 8);
    EXPECT_TRUE(rectEquals(rc, 1776, 16, 1904, 144));
}
