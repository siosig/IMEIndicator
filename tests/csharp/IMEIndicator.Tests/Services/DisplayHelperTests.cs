// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="DisplayHelper"/> のテスト。移植元: tests/cpp/unit/DisplayHelperTests.cpp。
/// T021 の指示どおり、実機のモニター構成（解像度・DPI・台数）には依存させず、
/// Windows 環境であれば常に成り立つはずの不変条件のみを検証する。
/// C++ 版は GTEST_SKIP でディスプレイ 0 件を許容していたが、稼働中の Windows デスクトップに
/// モニターが 1 台も無いことは実運用上ありえないため、本移植ではハードアサートに単純化する。
/// </summary>
public sealed class DisplayHelperTests
{
    [Fact]
    public void GetMonitors_ReturnsAtLeastOneMonitorWithValidGeometry()
    {
        var monitors = DisplayHelper.GetMonitors();

        Assert.NotEmpty(monitors);
        foreach (var m in monitors)
        {
            Assert.True(m.Width > 0, $"Width={m.Width}");
            Assert.True(m.Height > 0, $"Height={m.Height}");
            Assert.True(m.WorkRect.Width > 0, $"WorkRect.Width={m.WorkRect.Width}");
            Assert.True(m.WorkRect.Height > 0, $"WorkRect.Height={m.WorkRect.Height}");
            Assert.True(m.DpiX >= 96u, $"DpiX={m.DpiX}");
            Assert.True(m.DpiY >= 96u, $"DpiY={m.DpiY}");
        }
    }

    [Fact]
    public void GetMonitors_AtMostOneMonitorFlaggedPrimary()
    {
        var monitors = DisplayHelper.GetMonitors();

        // Windows は通常ちょうど 1 台をプライマリとして報告する。本テストでは
        // 「複数台が同時にプライマリを名乗ることはない」点のみを不変条件として検証する。
        var primaryCount = monitors.Count(m => m.IsPrimary);
        Assert.True(primaryCount is 0 or 1, $"primaryCount={primaryCount}");
    }

    [Fact]
    public void GetPrimaryMonitor_ReturnsValueWithValidGeometry()
    {
        var primary = DisplayHelper.GetPrimaryMonitor()
            ?? throw new InvalidOperationException("プライマリモニターが取得できない環境ではこのテストは成立しない。");

        Assert.True(primary.WorkRect.Width > 0, $"WorkRect.Width={primary.WorkRect.Width}");
        Assert.True(primary.WorkRect.Height > 0, $"WorkRect.Height={primary.WorkRect.Height}");
        Assert.True(primary.DpiX >= 96u, $"DpiX={primary.DpiX}");
        Assert.True(primary.DpiY >= 96u, $"DpiY={primary.DpiY}");
    }

    [Fact]
    public void GetPrimaryMonitor_PrimaryFlagged_ReturnsPrimaryEntry()
    {
        var monitors = DisplayHelper.GetMonitors();
        var anyPrimary = monitors.Any(m => m.IsPrimary);

        var primary = DisplayHelper.GetPrimaryMonitor()
            ?? throw new InvalidOperationException("プライマリモニターが取得できない環境ではこのテストは成立しない。");

        if (anyPrimary)
        {
            Assert.True(primary.IsPrimary);
        }
    }

    [Fact]
    public void GetPrimaryMonitor_NoneFlaggedPrimary_FallsBackToFirstEnumeratedMonitor()
    {
        var monitors = DisplayHelper.GetMonitors();
        if (monitors.Any(m => m.IsPrimary))
        {
            // このマシンには MONITORINFOF_PRIMARY を持つモニターがあるため、
            // フォールバック規則（先頭へフォールバック）はここでは検証できない
            // （現行 C++ 版もこの状況を GTEST_SKIP で許容していた箇所と同じ限界）。
            return;
        }

        var primary = DisplayHelper.GetPrimaryMonitor();

        Assert.Equal(monitors[0], primary);
    }

    [Fact]
    public void GetDpiForPoint_PrimaryMonitorCenter_MatchesPrimaryMonitorDpi()
    {
        var primary = DisplayHelper.GetPrimaryMonitor()
            ?? throw new InvalidOperationException("プライマリモニターが取得できない環境ではこのテストは成立しない。");

        var dpi = DisplayHelper.GetDpiForPoint(new Point(primary.CenterX, primary.CenterY));

        Assert.Equal(primary.DpiX, dpi);
    }

    [Fact]
    public void GetDpiForPoint_FarOffScreen_FallsBackToNearestMonitorDpi()
    {
        // MONITOR_DEFAULTTONEAREST を使うため、画面外の座標でも例外を投げず、
        // 何らかの有効な DPI（96 以上）を返す。
        var dpi = DisplayHelper.GetDpiForPoint(new Point(-99999, -99999));

        Assert.True(dpi >= 96u, $"dpi={dpi}");
    }

    [Fact]
    public void GetMonitors_WithIncludeDevicePath_AllMonitorsHaveNonEmptyIdentityKey()
    {
        var monitors = DisplayHelper.GetMonitors(includeDevicePath: true);
        Assert.NotEmpty(monitors);
        foreach (var m in monitors)
        {
            Assert.False(string.IsNullOrEmpty(m.IdentityKey));
        }
    }

    [Fact]
    public void GetMonitors_WithIncludeDevicePath_DevicePathIsEmptyOrDevicePathFormat()
    {
        var monitors = DisplayHelper.GetMonitors(includeDevicePath: true);
        foreach (var m in monitors)
        {
            Assert.True(m.DevicePath.Length == 0 || m.DevicePath.StartsWith(@"\\?\", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GetMonitors_WithoutIncludeDevicePath_DevicePathIsEmpty()
    {
        var monitors = DisplayHelper.GetMonitors();
        foreach (var m in monitors)
        {
            Assert.Equal(string.Empty, m.DevicePath);
        }
    }
}
