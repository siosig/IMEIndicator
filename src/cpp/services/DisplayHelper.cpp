#include "DisplayHelper.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <shellscalingapi.h>

#include <cmath>
#include <limits>

#pragma comment(lib, "shcore.lib")

namespace imeindicator::services {

namespace {

BOOL CALLBACK monitorEnumProc(HMONITOR hMon, HDC, LPRECT, LPARAM lp)
{
    auto* out = reinterpret_cast<std::vector<models::MonitorInfo>*>(lp);

    MONITORINFOEXW info{};
    info.cbSize = sizeof(info);
    if (!::GetMonitorInfoW(hMon, &info)) return TRUE;

    models::MonitorInfo m{};
    m.monitorRect = info.rcMonitor;
    m.workRect    = info.rcWork;
    m.isPrimary   = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
    m.deviceName  = info.szDevice;

    UINT dpiX = 96, dpiY = 96;
    // GetDpiForMonitor は失敗時に dpi を変更しないので 96 のままフォールバック。
    ::GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, &dpiX, &dpiY);
    m.dpiX = dpiX;
    m.dpiY = dpiY;

    out->push_back(std::move(m));
    return TRUE;
}

bool rectContains(const RECT& rc, double x, double y) noexcept
{
    return x >= rc.left && x < rc.right && y >= rc.top && y < rc.bottom;
}

} // namespace

std::vector<models::MonitorInfo> DisplayHelper::getAllMonitors()
{
    std::vector<models::MonitorInfo> out;
    ::EnumDisplayMonitors(nullptr, nullptr, &monitorEnumProc,
                          reinterpret_cast<LPARAM>(&out));
    return out;
}

size_t DisplayHelper::getScreenCount()
{
    return getAllMonitors().size();
}

DisplayHelper::ScreenBounds DisplayHelper::getScreenBounds(int index)
{
    auto monitors = getAllMonitors();
    if (index < 0 || static_cast<size_t>(index) >= monitors.size()) {
        return {};
    }
    const auto& rc = monitors[index].monitorRect;
    return {rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top};
}

int DisplayHelper::getDisplayIndexFromPosition(double x, double y,
                                               double width, double height)
{
    auto monitors = getAllMonitors();
    if (monitors.empty()) return -1;

    const double cx = x + width / 2.0;
    const double cy = y + height / 2.0;

    for (size_t i = 0; i < monitors.size(); ++i) {
        if (rectContains(monitors[i].monitorRect, cx, cy)) {
            return static_cast<int>(i);
        }
    }

    // 最近傍モニターをユークリッド距離で探す。
    int nearest = 0;
    double minDist = (std::numeric_limits<double>::max)();
    for (size_t i = 0; i < monitors.size(); ++i) {
        const auto& rc = monitors[i].monitorRect;
        const double sx = rc.left + (rc.right - rc.left) / 2.0;
        const double sy = rc.top + (rc.bottom - rc.top) / 2.0;
        const double d = std::sqrt((cx - sx) * (cx - sx) + (cy - sy) * (cy - sy));
        if (d < minDist) {
            minDist = d;
            nearest = static_cast<int>(i);
        }
    }
    return nearest;
}

bool DisplayHelper::isValidDisplayIndex(int displayIndex)
{
    auto monitors = getAllMonitors();
    return displayIndex >= 0 &&
           static_cast<size_t>(displayIndex) < monitors.size();
}

bool DisplayHelper::isPositionOnAnyDisplay(double x, double y)
{
    for (const auto& m : getAllMonitors()) {
        if (rectContains(m.monitorRect, x, y)) return true;
    }
    return false;
}

std::optional<models::MonitorInfo> DisplayHelper::getPrimaryMonitor()
{
    // 013-ime-corner-image: 毎回 EnumDisplayMonitors で列挙し直す（キャッシュしない）。
    auto monitors = getAllMonitors();
    if (monitors.empty()) return std::nullopt;

    for (auto& m : monitors) {
        if (m.isPrimary) return std::move(m);
    }
    // MONITORINFOF_PRIMARY が 1 つも無い場合は先頭（列挙順）へフォールバック。
    return std::move(monitors.front());
}

DisplayHelper::WorkArea DisplayHelper::getPrimaryWorkArea()
{
    // 探索規則（プライマリ → 先頭 → 無し）は getPrimaryMonitor() に一本化。
    const auto primary = getPrimaryMonitor();
    if (!primary) return {};

    const auto& rc = primary->workRect;
    return {rc.left, rc.top, rc.right, rc.bottom};
}

DisplayHelper::ValidPosition DisplayHelper::getValidPosition(
    double x, double y, double width, double height,
    int preferredDisplayIndex, bool useTopRight)
{
    auto monitors = getAllMonitors();
    if (monitors.empty()) return {x, y, 0};

    int target = preferredDisplayIndex;
    if (target < 0 || static_cast<size_t>(target) >= monitors.size()) {
        target = 0;
    }

    const double cx = x + width / 2.0;
    const double cy = y + height / 2.0;
    if (isPositionOnAnyDisplay(cx, cy)) {
        const int detected = getDisplayIndexFromPosition(x, y, width, height);
        return {x, y, detected >= 0 ? detected : target};
    }

    const auto& wa = monitors[target].workRect;
    constexpr double offset = 10.0;
    const double newX = useTopRight
                           ? (wa.right - width - offset)
                           : (wa.left + offset);
    const double newY = wa.top + offset;
    return {newX, newY, target};
}

} // namespace imeindicator::services
