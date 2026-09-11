// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Services;

/// <summary>
/// 背景画像ウィンドウの表示矩形計算（値オブジェクト・純関数）。
/// 現行 C++ 版 src/cpp/services/BackgroundImageLayout.h / .cpp（013-ime-corner-image /
/// spec FR-004, FR-011, FR-012）の移植。specs/013-ime-corner-image/data-model.md §3 の式を
/// そのまま実装する。Win32 API 呼び出しは一切行わない（単体テスト可能な純関数、T022）。
/// </summary>
/// <remarks>
/// 規則:
/// <code>
/// dpi    = (dpi == 0) ? 96 : dpi
/// size   = MulDiv(logicalSize,   dpi, 96)
/// margin = MulDiv(logicalMargin, dpi, 96)
/// avail  = min(work.width, work.height) - 2 * margin
/// if (size &gt; avail) size = max(1, avail)      // 極小作業領域では縮める
/// left   = work.right - margin - size
/// top    = work.top   + margin
/// return { left, top, left + size, top + size }
/// </code>
/// 呼び出し側（BackgroundImageWindow）は、モニター構成変更（WM_DISPLAYCHANGE / WM_DPICHANGED /
/// WM_SETTINGCHANGE(SPI_SETWORKAREA)）のたびに本関数で矩形を再計算する（FR-011）。
/// </remarks>
public static class BackgroundImageLayout
{
    /// <summary>表示サイズの既定値（論理 px 四方、FR-012）。</summary>
    public const int DefaultLogicalSize = 128;

    /// <summary>作業領域の右端・上端からの余白の既定値（論理 px、FR-012）。</summary>
    public const int DefaultLogicalMargin = 16;

    // 論理 px の基準 DPI（100%）。
    private const int BaseDpi = 96;

    /// <summary>
    /// 背景画像ウィンドウの表示矩形を計算する。
    /// </summary>
    /// <param name="workArea">プライマリモニターの作業領域（タスクバー除外、物理 px、仮想スクリーン座標）。</param>
    /// <param name="dpi">表示先モニターの有効 DPI（GetDpiForMonitor の MDT_EFFECTIVE_DPI）。0 は 96 として扱う。</param>
    /// <param name="logicalSize">一辺の論理 px（非負）。省略時は <see cref="DefaultLogicalSize"/>。</param>
    /// <param name="logicalMargin">右端・上端からの余白の論理 px（非負）。省略時は <see cref="DefaultLogicalMargin"/>。</param>
    /// <returns>ウィンドウ矩形（物理 px、仮想スクリーン座標）。作業領域の右上、正方形。</returns>
    public static Rectangle Compute(
        Rectangle workArea,
        uint dpi,
        int logicalSize = DefaultLogicalSize,
        int logicalMargin = DefaultLogicalMargin)
    {
        // GetDpiForMonitor 失敗時などの 0 は 100% として扱う。
        var effectiveDpi = dpi == 0 ? BaseDpi : (int)dpi;

        // MulDiv は 64bit 中間値で乗算してから除算し、最近接整数に丸める（切り捨てではない）。
        // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-muldiv
        var size = MulDiv(logicalSize, effectiveDpi, BaseDpi);
        var margin = MulDiv(logicalMargin, effectiveDpi, BaseDpi);

        // 作業領域の短辺から両側の余白を引いた範囲に収める（極小作業領域では縮める。最小 1 px）。
        var avail = Math.Min(workArea.Width, workArea.Height) - 2 * margin;
        if (size > avail)
        {
            size = Math.Max(1, avail);
        }

        // 右上基準: 右端から余白ぶん内側、上端から余白ぶん下。
        var left = workArea.Right - margin - size;
        var top = workArea.Top + margin;
        return new Rectangle(left, top, size, size);
    }

    // Win32 API MulDiv の算術を再現する（本関数は「Win32 呼び出しなし」の要件のため実装を複製する）。
    // 64bit 中間値で乗算し、積の符号に応じて ± denominator/2 を加えてから整数除算する
    // （C# の整数除算は 0 方向切り捨てのため、これと組み合わせて「最も近い整数への丸め」になる）。
    // 本関数の実引数は常に非負（logicalSize/logicalMargin は非負、effectiveDpi・BaseDpi は正）だが、
    // 一般の MulDiv 実装（Wine 等）に倣い符号付きでも正しく丸まるようにしておく。
    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-muldiv
    private static int MulDiv(int number, int numerator, int denominator)
    {
        var product = (long)number * numerator;
        var half = denominator / 2;
        var adjusted = product >= 0 ? product + half : product - half;
        return (int)(adjusted / denominator);
    }
}
