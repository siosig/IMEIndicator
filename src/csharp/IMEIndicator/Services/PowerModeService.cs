// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// Windows 11 電源モード（Power Overlay Scheme）の取得・設定・トグル。
/// 移植元: src/cpp/services/PowerModeService.h / .cpp の struct PowerModeService。
/// PowerSetActiveOverlayScheme / PowerGetActualOverlayScheme（powrprof.dll の undocumented API。
/// IMEIndicator.Interop.NativeMethods.PowrProf.cs 参照）を直接呼び出す。GUID の対応は
/// NativeTypes.cs の NativeConstants.GuidPowerOverlay* 定数を使う（PowerModeService.cpp の
/// NativeConstants.h と同値）。
/// </summary>
public static class PowerModeService
{
    // ERROR_SUCCESS（winerror.h）。PowerSetActiveOverlayScheme / PowerGetActualOverlayScheme は
    // どちらも Win32 エラーコード（成功時 0）を返す。
    private const uint ErrorSuccess = 0;

    /// <summary>
    /// 現在の電源モードを取得する。API 呼び出しに失敗した場合は Balanced を返す
    /// （移植元 getCurrentMode() の「取得失敗時は Balanced」という仕様のまま）。
    /// </summary>
    public static PowerMode GetCurrent()
    {
        uint result = NativeMethods.PowerGetActualOverlayScheme(out Guid current);
        if (result != ErrorSuccess)
        {
            return PowerMode.Balanced;
        }

        return GuidToPowerMode(current);
    }

    /// <summary>指定した電源モードへ切り替える。成功時 true。</summary>
    public static bool Set(PowerMode mode)
    {
        uint result = NativeMethods.PowerSetActiveOverlayScheme(PowerModeToGuid(mode));
        return result == ErrorSuccess;
    }

    /// <summary>
    /// 電源モードをトグルする（移植元 toggleMode() のロジックをそのまま）。
    ///   最適な電力効率 → バランス
    ///   バランス       → 最適な電力効率
    ///   最適なパフォーマンス → バランス
    /// 戻り値は遷移先のモード（Set が失敗しても、移植元と同じく論理上の遷移先を返す）。
    /// </summary>
    public static PowerMode Toggle()
    {
        PowerMode current = GetCurrent();
        PowerMode next = current == PowerMode.BestPowerEfficiency
            ? PowerMode.Balanced
            : PowerMode.BestPowerEfficiency;
        if (current == PowerMode.BestPerformance)
        {
            // 最適なパフォーマンスからはバランスへ戻す（既存仕様）。
            next = PowerMode.Balanced;
        }

        Set(next);
        return next;
    }

    /// <summary>
    /// 電源モードの表示名（トレイメニュー・バルーン通知用）。ui-parity-contract.md §1 のメニュー文言、
    /// 移植元 <c>PowerModeService::getDisplayName</c>（<c>models::powerModeDisplayName</c>）と同じ。
    /// </summary>
    public static string GetDisplayName(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => "最適な電力効率",
        PowerMode.Balanced => "バランス",
        PowerMode.BestPerformance => "最適なパフォーマンス",
        _ => "バランス",
    };

    private static Guid PowerModeToGuid(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => NativeConstants.GuidPowerOverlayBestPowerEfficiency,
        PowerMode.Balanced => NativeConstants.GuidPowerOverlayBalanced,
        PowerMode.BestPerformance => NativeConstants.GuidPowerOverlayBestPerformance,
        _ => NativeConstants.GuidPowerOverlayBalanced,
    };

    private static PowerMode GuidToPowerMode(Guid guid)
    {
        if (guid == NativeConstants.GuidPowerOverlayBestPowerEfficiency)
        {
            return PowerMode.BestPowerEfficiency;
        }

        if (guid == NativeConstants.GuidPowerOverlayBestPerformance)
        {
            return PowerMode.BestPerformance;
        }

        return PowerMode.Balanced;
    }
}
