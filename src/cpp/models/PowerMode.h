#pragma once

#include <string>
#include <string_view>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <guiddef.h>

namespace imeindicator::models {

// Windows 11 電源モード（Overlay Power Scheme）
// 既存 C# 版 PowerModeService.cs と完全互換
enum class PowerMode : int {
    BestPowerEfficiency = 0,
    Balanced            = 1,
    BestPerformance     = 2
};

// 表示名（日本語固定）
inline std::wstring_view powerModeDisplayName(PowerMode mode)
{
    switch (mode) {
        case PowerMode::BestPowerEfficiency: return L"最適な電力効率";
        case PowerMode::Balanced:            return L"バランス";
        case PowerMode::BestPerformance:     return L"最適なパフォーマンス";
    }
    return L"不明";
}

// PowerModeBackup 用の安定文字列（永続化キー、変更不可）
inline std::string powerModeToStableString(PowerMode mode)
{
    switch (mode) {
        case PowerMode::BestPowerEfficiency: return "BestPowerEfficiency";
        case PowerMode::Balanced:            return "Balanced";
        case PowerMode::BestPerformance:     return "BestPerformance";
    }
    return "Balanced";
}

// 安定文字列 → PowerMode。範囲外は std::nullopt 相当（戻り値 false）。
inline bool tryParsePowerMode(std::string_view s, PowerMode& out)
{
    if (s == "BestPowerEfficiency") { out = PowerMode::BestPowerEfficiency; return true; }
    if (s == "Balanced")            { out = PowerMode::Balanced;            return true; }
    if (s == "BestPerformance")     { out = PowerMode::BestPerformance;     return true; }
    return false;
}

// 電源モードのインジケーター色（HEX 文字列、tray-ui-contract.md 準拠）
inline std::wstring_view powerModeIndicatorColorHex(PowerMode mode)
{
    switch (mode) {
        case PowerMode::BestPowerEfficiency: return L"#3B82F6";
        case PowerMode::Balanced:            return L"#EF4444";
        case PowerMode::BestPerformance:     return L"#EAB308";
    }
    return L"#EF4444";
}

// 電源モードに対応する Power Overlay GUID 取得（NativeConstants から取り出し）
const GUID& powerModeToOverlayGuid(PowerMode mode);

// Overlay GUID → PowerMode（GUID_NULL 等は Balanced 扱い）
PowerMode overlayGuidToPowerMode(const GUID& guid);

} // namespace imeindicator::models
