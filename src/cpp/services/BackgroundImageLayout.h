#pragma once

// 013-ime-corner-image / spec FR-004, FR-011, FR-012
// 背景画像ウィンドウの表示矩形を求める純関数（data-model.md §3、research.md R-5）。
//
// 規則:
//   dpi    = (dpi == 0) ? 96 : dpi
//   size   = MulDiv(logicalSize,   dpi, 96)
//   margin = MulDiv(logicalMargin, dpi, 96)
//   avail  = min(work.width, work.height) - 2 * margin
//   if (size > avail) size = max(1, avail)      // 極小作業領域では縮める
//   left   = work.right - margin - size
//   top    = work.top   + margin
//   return { left, top, left + size, top + size }
//
// 呼び出し側（BackgroundImageWindow::relayout）は、モニター構成変更（WM_DISPLAYCHANGE /
// WM_DPICHANGED / WM_SETTINGCHANGE(SPI_SETWORKAREA)）のたびに本関数で矩形を再計算する（FR-011）。

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// 背景画像の配置計算（値オブジェクト・純関数）。
// Win32 呼び出しは MulDiv のみで、モニター列挙やウィンドウ操作は行わない（単体テスト可能）。
struct BackgroundImageLayout {
    static constexpr int kLogicalSize   = 128;   // FR-012: 表示サイズ 128 論理 px 四方
    static constexpr int kLogicalMargin = 16;    // FR-012: 作業領域の右端・上端からの余白 16 論理 px

    // workArea:      プライマリモニターの作業領域（タスクバー除外、物理 px、仮想スクリーン座標）
    // dpi:           表示先モニターの有効 DPI（GetDpiForMonitor の MDT_EFFECTIVE_DPI）。0 は 96 として扱う
    // logicalSize:   一辺の論理 px（非負）
    // logicalMargin: 右端・上端からの余白の論理 px（非負）
    // 戻り値:        ウィンドウ矩形（物理 px、仮想スクリーン座標）。作業領域の右上、正方形
    static RECT compute(const RECT& workArea, UINT dpi,
                        int logicalSize = kLogicalSize, int logicalMargin = kLogicalMargin) noexcept;
};

} // namespace imeindicator::services
