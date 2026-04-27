#include "PowerMode.h"

#include "../win32/NativeConstants.h"

namespace imeindicator::models {

const GUID& powerModeToOverlayGuid(PowerMode mode)
{
    using namespace imeindicator::win32;
    switch (mode) {
        case PowerMode::BestPowerEfficiency: return GUID_POWER_OVERLAY_BEST_POWER_EFFICIENCY;
        case PowerMode::Balanced:            return GUID_POWER_OVERLAY_BALANCED;
        case PowerMode::BestPerformance:     return GUID_POWER_OVERLAY_BEST_PERFORMANCE;
    }
    return GUID_POWER_OVERLAY_BALANCED;
}

PowerMode overlayGuidToPowerMode(const GUID& guid)
{
    using namespace imeindicator::win32;
    if (IsEqualGUID(guid, GUID_POWER_OVERLAY_BEST_POWER_EFFICIENCY))
        return PowerMode::BestPowerEfficiency;
    if (IsEqualGUID(guid, GUID_POWER_OVERLAY_BEST_PERFORMANCE))
        return PowerMode::BestPerformance;
    return PowerMode::Balanced;
}

} // namespace imeindicator::models
