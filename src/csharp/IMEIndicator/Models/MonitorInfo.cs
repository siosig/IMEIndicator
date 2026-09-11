// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Models;

/// <summary>
/// 単一ディスプレイの幾何情報（data-model.md §3 実行時モデル）。
/// 現行 C++ 版 src/cpp/models/MonitorInfo.h と等価。DisplayHelper が
/// EnumDisplayMonitors / GetDpiForMonitor の結果から構築する（Services 層、本タスクの対象外）。
/// Rectangle は System.Drawing（WinForms SDK の暗黙 using）をそのまま用いる。
/// </summary>
/// <param name="MonitorRect">物理ピクセル境界（仮想スクリーン座標）。</param>
/// <param name="WorkRect">タスクバー除外領域。</param>
/// <param name="IsPrimary">プライマリモニターかどうか。</param>
/// <param name="DpiX">横方向 DPI（既定 96 = 100%）。</param>
/// <param name="DpiY">縦方向 DPI（既定 96 = 100%）。</param>
/// <param name="DeviceName">ディスプレイデバイス名（例: \\.\DISPLAY1）。</param>
public sealed record MonitorInfo(
    Rectangle MonitorRect,
    Rectangle WorkRect,
    bool IsPrimary,
    uint DpiX = 96,
    uint DpiY = 96,
    string DeviceName = "")
{
    /// <summary>中心座標 X（物理ピクセル）。</summary>
    public int CenterX => (MonitorRect.Left + MonitorRect.Right) / 2;

    /// <summary>中心座標 Y（物理ピクセル）。</summary>
    public int CenterY => (MonitorRect.Top + MonitorRect.Bottom) / 2;

    /// <summary>矩形の幅。</summary>
    public int Width => MonitorRect.Width;

    /// <summary>矩形の高さ。</summary>
    public int Height => MonitorRect.Height;
}
