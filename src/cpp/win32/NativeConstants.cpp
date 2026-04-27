#include <initguid.h>  // DEFINE_GUID をインライン定義に切り替える

#include "NativeConstants.h"

namespace imeindicator::win32 {

// 既存 C# 版 PowerModeService.cs の Guid 文字列と完全一致。
// Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")
DEFINE_GUID(GUID_POWER_OVERLAY_BEST_POWER_EFFICIENCY,
            0x961cc777, 0x2547, 0x4f9d, 0x81, 0x74, 0x7d, 0x86, 0x18, 0x1b, 0x8a, 0x7a);

// Guid.Empty
DEFINE_GUID(GUID_POWER_OVERLAY_BALANCED,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

// Guid("ded574b5-45a0-4f42-8737-46345c09c238")
DEFINE_GUID(GUID_POWER_OVERLAY_BEST_PERFORMANCE,
            0xded574b5, 0x45a0, 0x4f42, 0x87, 0x37, 0x46, 0x34, 0x5c, 0x09, 0xc2, 0x38);

const PowerOverlayApi& PowerOverlayApi::instance() noexcept
{
    // 1 度だけ powrprof.dll をロード（プロセスが終了するまで保持）。
    // FreeLibrary は意図的に呼ばない: トレイ常駐プロセスの寿命と同じ。
    static const PowerOverlayApi api = []() noexcept {
        PowerOverlayApi a{};
        HMODULE h = ::LoadLibraryW(L"powrprof.dll");
        if (!h) {
            return a;
        }
        a.setActive = reinterpret_cast<PFN_PowerSetActiveOverlayScheme>(
            ::GetProcAddress(h, "PowerSetActiveOverlayScheme"));
        a.getActual = reinterpret_cast<PFN_PowerGetActualOverlayScheme>(
            ::GetProcAddress(h, "PowerGetActualOverlayScheme"));
        return a;
    }();
    return api;
}

} // namespace imeindicator::win32
