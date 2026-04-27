#pragma once

#include "../models/PowerMode.h"

#include <string>
#include <string_view>

namespace imeindicator::services {

// Windows 11 電源モード（Overlay）の取得・設定・トグル。
// 既存 C# 版 [PowerModeService.cs] と等価動作を提供する。
// 内部実装は win32::PowerOverlayApi（powrprof.dll の動的解決）に委譲。
struct PowerModeService {
    // 現在の電源モード。取得失敗時は Balanced。
    static models::PowerMode getCurrentMode() noexcept;

    // 指定モードへ設定。成功時 true。
    static bool setMode(models::PowerMode mode) noexcept;

    // 電源モードをトグル：
    //   BestPowerEfficiency → Balanced
    //   Balanced            → BestPowerEfficiency
    //   BestPerformance     → Balanced
    // 戻り値は遷移先のモード（setMode 失敗時も論理上の遷移先を返す）。
    static models::PowerMode toggleMode() noexcept;

    // 電源モードに対応するインジケーター色（HEX 文字列、tray-ui-contract.md §インジケーター色）
    static std::string_view getIndicatorColorHex(models::PowerMode mode) noexcept;

    // 表示名（日本語固定）
    static std::wstring_view getDisplayName(models::PowerMode mode) noexcept;
};

} // namespace imeindicator::services
