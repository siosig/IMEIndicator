// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// マルチディスプレイ関連のヘルパー。現行 C++ 版 src/cpp/services/DisplayHelper.h / .cpp のうち、
/// <see cref="GetMonitors"/> / <see cref="GetPrimaryMonitor"/> / DPI 取得（T021）を移植する。
/// C++ 版が持つ getScreenBounds / getDisplayIndexFromPosition / isPositionOnAnyDisplay /
/// getValidPosition 等（HotkeyP 由来のウィンドウ配置ロジック）は、013-ime-corner-image の
/// 背景画像ウィンドウ・カーソル追従インジケーターが必要とする範囲を超えるため、
/// 本タスクでは対象外（利用側が必要になった時点で追加する）。
/// </summary>
/// <remarks>
/// 内部で EnumDisplayMonitors + GetMonitorInfoW + GetDpiForMonitor を毎回呼び直す
/// （結果をキャッシュしない）。頻繁な呼び出しは避け、必要なら呼び出し側でキャッシュすること
/// （現行 C++ 版 DisplayHelper と同じ運用規則）。
/// </remarks>
public static class DisplayHelper
{
    /// <summary>
    /// 全モニター情報を取得する（プライマリも含む）。EnumDisplayMonitors の列挙順を保持する。
    /// </summary>
    /// <param name="includeDevicePath">
    /// true の場合、各モニターの <see cref="MonitorInfo.DevicePath"/> を <see cref="GetDevicePath"/>
    /// （EnumDisplayDevicesW）で解決する。既定は false。未移動のユーザーに追加の API 呼び出しを
    /// 発生させないため、DevicePath が必要な呼び出し側だけが true を渡す
    /// （018-draggable-background-image、research.md R-5）。
    /// </param>
    public static IReadOnlyList<MonitorInfo> GetMonitors(bool includeDevicePath = false)
    {
        var monitors = new List<MonitorInfo>();

        bool EnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData)
        {
            var info = MONITORINFOEXW.Create();
            if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            {
                // 取得に失敗したモニターは無視して列挙を継続する（DisplayHelper.cpp と同じ）。
                return true;
            }

            GetEffectiveDpi(hMonitor, out var dpiX, out var dpiY);
            var devicePath = includeDevicePath ? GetDevicePath(info.SzDevice) : string.Empty;

            monitors.Add(new MonitorInfo(
                MonitorRect: ToRectangle(info.RcMonitor),
                WorkRect: ToRectangle(info.RcWork),
                IsPrimary: (info.DwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0,
                DpiX: dpiX,
                DpiY: dpiY,
                DeviceName: info.SzDevice,
                DevicePath: devicePath));
            return true;
        }

        NativeMethods.EnumDisplayMonitors(0, 0, EnumProc, 0);
        return monitors;
    }

    /// <summary>
    /// プライマリモニターを取得する。<c>MONITORINFOF_PRIMARY</c> フラグを持つモニターを優先し、
    /// 見つからない場合は列挙順の先頭にフォールバックする
    /// （DisplayHelper.cpp::getPrimaryMonitor と同じ規則）。モニターが 1 つもない場合は <c>null</c>。
    /// </summary>
    public static MonitorInfo? GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }
        return monitors[0];
    }

    /// <summary>
    /// 指定したモニターハンドルの有効 DPI を返す（GetDpiForMonitor ラッパー）。
    /// 取得に失敗した場合は 96（100%）にフォールバックする。
    /// </summary>
    public static uint GetDpiForMonitor(nint hMonitor)
    {
        GetEffectiveDpi(hMonitor, out var dpiX, out _);
        return dpiX;
    }

    /// <summary>
    /// 指定した仮想スクリーン座標を含む（範囲外なら最も近い）モニターの有効 DPI を返す。
    /// <c>MonitorFromPoint(MONITOR_DEFAULTTONEAREST)</c> + <see cref="GetDpiForMonitor(nint)"/>。
    /// </summary>
    public static uint GetDpiForPoint(Point point)
    {
        var pt = new POINT { X = point.X, Y = point.Y };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeConstants.MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(hMonitor);
    }

    // GetDpiForMonitor(shcore.dll) の生呼び出し。失敗時は 96/96 にフォールバックする。
    // out 引数は P/Invoke 呼び出し前の C# 側の値を引き継がない（ネイティブ側が書かなければ
    // 既定の 0 のまま返る）ため、C++ 版のような「事前に 96 を代入しておくフォールバック」は
    // 使えない。戻り値（HRESULT）を見て明示的にフォールバックする。
    // https://learn.microsoft.com/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor
    private static void GetEffectiveDpi(nint hMonitor, out uint dpiX, out uint dpiY)
    {
        var hr = NativeMethods.GetDpiForMonitor(hMonitor, NativeConstants.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
        if (hr != 0) // S_OK 以外は失敗として扱う
        {
            dpiX = 96;
            dpiY = 96;
        }
    }

    private static Rectangle ToRectangle(RECT rect) => Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

    // モニターのデバイスインターフェースパス（DevicePath）を解決する（018-draggable-background-image、research.md R-5）。
    // EnumDisplayDevicesW に EDD_GET_DEVICE_INTERFACE_NAME を指定すると、GUID_DEVINTERFACE_MONITOR の
    // デバイスインターフェース名が DISPLAY_DEVICE.DeviceID に格納される。
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw
    // PowerToys FancyZones（MonitorUtils.cpp の IdentifyMonitors）と同じ方式：DISPLAY_DEVICE_ACTIVE が立ち、
    // DISPLAY_DEVICE_MIRRORING_DRIVER が立っていない最初のエントリの DeviceID を採用する。
    // https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/MonitorUtils.cpp
    private static string GetDevicePath(string deviceName)
    {
        for (uint iDevNum = 0; ; iDevNum++)
        {
            var device = DISPLAY_DEVICEW.Create();
            if (!NativeMethods.EnumDisplayDevicesW(deviceName, iDevNum, ref device, NativeConstants.EDD_GET_DEVICE_INTERFACE_NAME))
            {
                // これ以上のエントリが無い。DevicePath は空文字のまま扱う（例外にしない）。
                return string.Empty;
            }

            var isActive = (device.StateFlags & NativeConstants.DISPLAY_DEVICE_ACTIVE) != 0;
            var isMirroring = (device.StateFlags & NativeConstants.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0;
            if (isActive && !isMirroring)
            {
                return device.DeviceID;
            }
        }
    }
}
