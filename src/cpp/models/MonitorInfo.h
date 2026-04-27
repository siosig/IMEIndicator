#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <string>

namespace imeindicator::models {

// 単一ディスプレイの幾何情報（data-model.md §6）
struct MonitorInfo {
    RECT monitorRect{};   // 物理ピクセル境界（仮想スクリーン座標）
    RECT workRect{};      // タスクバー除外領域
    bool isPrimary{false};
    UINT dpiX{96};
    UINT dpiY{96};
    std::wstring deviceName;

    // 中心座標（物理ピクセル）
    LONG centerX() const noexcept { return (monitorRect.left + monitorRect.right) / 2; }
    LONG centerY() const noexcept { return (monitorRect.top + monitorRect.bottom) / 2; }

    // 矩形の幅・高さ
    LONG width() const noexcept { return monitorRect.right - monitorRect.left; }
    LONG height() const noexcept { return monitorRect.bottom - monitorRect.top; }
};

} // namespace imeindicator::models
