// 013-ime-corner-image / spec FR-004, FR-011, FR-012
// 背景画像ウィンドウの表示矩形計算（data-model.md §3、research.md R-5）。
// 規則の要約: DPI で 128/16 論理 px を物理 px に換算し、作業領域の右上に配置する。
// 作業領域の短辺に収まらない場合は縮める（最小 1 px）。dpi == 0 は 96 扱い。
#include "BackgroundImageLayout.h"

#include <algorithm>

namespace imeindicator::services {

namespace {

// 論理 px の基準 DPI（100%）。
constexpr int kBaseDpi = 96;

} // namespace

RECT BackgroundImageLayout::compute(const RECT& workArea, UINT dpi,
                                    int logicalSize, int logicalMargin) noexcept
{
    // GetDpiForMonitor 失敗時などの 0 は 100% として扱う。
    const int effectiveDpi = (dpi == 0) ? kBaseDpi : static_cast<int>(dpi);

    // MulDiv は 64bit 中間値で乗算してから除算し、最近接整数に丸める（切り捨てではない）。
    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-muldiv
    // 入力は論理 px（128/16）と DPI（最大でも数百）なので 32bit 溢れ（戻り値 -1）は起きない。
    int size         = ::MulDiv(logicalSize, effectiveDpi, kBaseDpi);
    const int margin = ::MulDiv(logicalMargin, effectiveDpi, kBaseDpi);

    // 作業領域の短辺から両側の余白を引いた範囲に収める（極小作業領域では縮める。最小 1 px）。
    const LONG width  = workArea.right - workArea.left;
    const LONG height = workArea.bottom - workArea.top;
    const int avail   = static_cast<int>(std::min(width, height)) - 2 * margin;
    if (size > avail) {
        size = std::max(1, avail);
    }

    // 右上基準: 右端から余白ぶん内側、上端から余白ぶん下。
    const LONG left = workArea.right - margin - size;
    const LONG top  = workArea.top + margin;
    return RECT{left, top, left + size, top + size};
}

} // namespace imeindicator::services
