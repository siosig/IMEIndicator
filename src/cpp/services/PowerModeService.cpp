#include "PowerModeService.h"

#include "../win32/NativeConstants.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

models::PowerMode PowerModeService::getCurrentMode() noexcept
{
    using namespace imeindicator::win32;
    const auto& api = PowerOverlayApi::instance();
    if (!api.ready()) return models::PowerMode::Balanced;

    GUID current = GUID_POWER_OVERLAY_BALANCED;
    if (api.getActual(&current) != ERROR_SUCCESS) return models::PowerMode::Balanced;
    return models::overlayGuidToPowerMode(current);
}

bool PowerModeService::setMode(models::PowerMode mode) noexcept
{
    using namespace imeindicator::win32;
    const auto& api = PowerOverlayApi::instance();
    if (!api.ready()) return false;

    return api.setActive(models::powerModeToOverlayGuid(mode)) == ERROR_SUCCESS;
}

models::PowerMode PowerModeService::toggleMode() noexcept
{
    auto current = getCurrentMode();
    auto next = (current == models::PowerMode::BestPowerEfficiency)
                ? models::PowerMode::Balanced
                : models::PowerMode::BestPowerEfficiency;
    if (current == models::PowerMode::BestPerformance) {
        // 最適なパフォーマンスからはバランスへ戻す（既存仕様）。
        next = models::PowerMode::Balanced;
    }
    setMode(next);
    return next;
}

std::string_view PowerModeService::getIndicatorColorHex(models::PowerMode mode) noexcept
{
    switch (mode) {
        case models::PowerMode::BestPowerEfficiency: return "#3B82F6";
        case models::PowerMode::BestPerformance:     return "#EAB308";
        case models::PowerMode::Balanced:            return "#EF4444";
    }
    return "#EF4444";
}

std::wstring_view PowerModeService::getDisplayName(models::PowerMode mode) noexcept
{
    return models::powerModeDisplayName(mode);
}

} // namespace imeindicator::services
