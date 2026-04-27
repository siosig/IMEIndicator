#include "EntryPoint_PowerToggle.h"

#include "AppConstants.h"
#include "../win32/NativeConstants.h"

#include <string>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::app {

int runPowerToggleEntry()
{
    // Phase 5 で PowerModeService と統合するまでの最小実装。
    // contracts/ipc-contract.md §3 に従い、メインインスタンスへ Event を立てるか、
    // フォールバックで自前トグルを行う。

    // 1) Event を OPEN しに行く（メインインスタンス側が CreateEventW で待機している）
    HANDLE hEvent = ::OpenEventW(EVENT_MODIFY_STATE, FALSE,
                                  std::wstring(AppConstants::PowerToggleEventName).c_str());
    if (hEvent != nullptr) {
        ::SetEvent(hEvent);
        ::CloseHandle(hEvent);
        return 0;
    }

    // 2) フォールバック: 直接トグル（Best Power Efficiency ↔ Balanced）
    using namespace imeindicator::win32;
    const auto& power = PowerOverlayApi::instance();
    if (!power.ready()) {
        return 1;  // powrprof.dll の解決に失敗
    }

    GUID current = GUID_POWER_OVERLAY_BALANCED;
    if (power.getActual(&current) != ERROR_SUCCESS) {
        return 1;
    }

    GUID next;
    if (IsEqualGUID(current, GUID_POWER_OVERLAY_BEST_POWER_EFFICIENCY)) {
        next = GUID_POWER_OVERLAY_BALANCED;
    } else if (IsEqualGUID(current, GUID_POWER_OVERLAY_BEST_PERFORMANCE)) {
        // パフォーマンスからはバランスへ（仕様書）
        next = GUID_POWER_OVERLAY_BALANCED;
    } else {
        next = GUID_POWER_OVERLAY_BEST_POWER_EFFICIENCY;
    }

    if (power.setActive(next) != ERROR_SUCCESS) {
        return 1;
    }

    // バルーン通知は Phase 3 (TrayIcon 完成後) で配線する。
    return 0;
}

} // namespace imeindicator::app
