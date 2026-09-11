// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Models;

/// <summary>
/// Windows 11 の電源モード（Power Overlay Scheme）。
/// 現行 C++ 版 src/cpp/models/PowerMode.h と等価。C# 版では Power Overlay の GUID は
/// 保持せず（GUID 変換は Services 層の PowerModeService が Win32 API 呼び出し時に行う）、
/// enum と表示色のみをこのモデルで扱う。
/// </summary>
public enum PowerMode
{
    /// <summary>最適な電力効率。</summary>
    BestPowerEfficiency = 0,

    /// <summary>バランス。</summary>
    Balanced = 1,

    /// <summary>最適なパフォーマンス。</summary>
    BestPerformance = 2,
}

/// <summary>
/// <see cref="PowerMode"/> に付随する表示情報。
/// </summary>
public static class PowerModeColors
{
    /// <summary>
    /// 電源モードに対応するインジケーター色（HEX 文字列）を返す。
    /// 現行 C++ 版 PowerModeService::getIndicatorColorHex
    /// （src/cpp/services/PowerModeService.cpp）と同じ対応。
    /// 未定義の値が渡された場合は Balanced と同じ色にフォールバックする（現行の default 分岐と同じ）。
    /// </summary>
    public static string GetIndicatorColorHex(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => "#3B82F6",
        PowerMode.Balanced => "#EF4444",
        PowerMode.BestPerformance => "#EAB308",
        _ => "#EF4444",
    };
}
