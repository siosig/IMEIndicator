// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;
using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="BackgroundImagePlacement"/> の単体テスト。
/// 契約: specs/018-draggable-background-image/contracts/background-image-placement-contract.md。
/// 同契約の「テスト観点」節と、不変条件 I-1〜I-6 を網羅する。Win32 API 呼び出しの無い純関数なので、
/// ディスプレイ構成に依存せず常に実行できる（BackgroundImageLayoutTests と同じ方針）。
/// </summary>
public sealed class BackgroundImagePlacementTests
{
    // ============================================================
    // Resolve: I-1（position == null のとき、BackgroundImageLayout.Compute と完全一致）
    // ============================================================

    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void Resolve_NullPosition_MatchesBackgroundImageLayoutCompute_AcrossDpi(uint dpi)
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var primary = Mon(work: work, isPrimary: true, dpi: dpi, devicePath: "PRIMARY");
        var monitors = new[] { primary };

        var expected = BackgroundImageLayout.Compute(primary.WorkRect, primary.DpiX, BackgroundImageLayout.DefaultLogicalSize, BackgroundImageLayout.DefaultLogicalMargin);
        var result = BackgroundImagePlacement.Resolve(monitors, null, BackgroundImageLayout.DefaultLogicalSize, BackgroundImageLayout.DefaultLogicalMargin);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Rect);
        Assert.Equal(primary, result.Monitor);
        Assert.False(result.UsedPrimaryFallback);
    }

    [Fact]
    public void Resolve_NullPosition_MatchesCompute_WithNegativeCoordinateWorkArea()
    {
        // マルチモニターで対象モニターの左に別モニターがある想定（プライマリの作業領域が負の座標を含む）。
        // BackgroundImageLayoutTests の「負座標のプライマリ」ケースと同じ work 座標を使う。
        // プライマリをリストの2番目に置き、Primary() が IsPrimary を正しく探索することも兼ねて検証する。
        var primary = Mon(work: Rectangle.FromLTRB(-2560, 0, 0, 1440), isPrimary: true, dpi: 96, devicePath: "PRIMARY");
        var secondary = Mon(work: Rectangle.FromLTRB(-5120, 0, -2560, 1440), isPrimary: false, dpi: 96, devicePath: "SECONDARY");
        var monitors = new[] { secondary, primary };

        var expected = BackgroundImageLayout.Compute(primary.WorkRect, primary.DpiX, BackgroundImageLayout.DefaultLogicalSize, BackgroundImageLayout.DefaultLogicalMargin);
        var result = BackgroundImagePlacement.Resolve(monitors, null, BackgroundImageLayout.DefaultLogicalSize, BackgroundImageLayout.DefaultLogicalMargin);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Rect);
        Assert.Equal(primary, result.Monitor);
        Assert.False(result.UsedPrimaryFallback);
    }

    // ============================================================
    // Resolve: 隅の座標、DPI による距離・大きさの拡縮、丸め
    // ============================================================

    [Theory]
    [InlineData(BackgroundImageAnchor.TopLeft, 24, 16)]
    [InlineData(BackgroundImageAnchor.TopRight, 1768, 16)]
    [InlineData(BackgroundImageAnchor.BottomLeft, 24, 936)]
    [InlineData(BackgroundImageAnchor.BottomRight, 1768, 936)]
    public void Resolve_DetectsCorrectCornerCoordinates(BackgroundImageAnchor anchor, int expX, int expY)
    {
        var monitor = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, dpi: 96, devicePath: "MON-1");
        var position = new BackgroundImagePosition("MON-1", anchor, 24.0, 16.0);

        var result = BackgroundImagePlacement.Resolve(new[] { monitor }, position, 128, 16);

        Assert.NotNull(result);
        Assert.Equal(new Rectangle(expX, expY, 128, 128), result!.Rect);
        Assert.False(result.UsedPrimaryFallback);
    }

    [Theory]
    [InlineData(96u, 24, 16, 128)]
    [InlineData(144u, 36, 24, 192)]
    [InlineData(192u, 48, 32, 256)]
    public void Resolve_ScalesDistanceAndSizeWithDpi(uint dpi, int expX, int expY, int expSize)
    {
        var monitor = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, dpi: dpi, devicePath: "MON-1");
        var position = new BackgroundImagePosition("MON-1", BackgroundImageAnchor.TopLeft, 24.0, 16.0);

        var result = BackgroundImagePlacement.Resolve(new[] { monitor }, position, 128, 16);

        Assert.NotNull(result);
        Assert.Equal(new Rectangle(expX, expY, expSize, expSize), result!.Rect);
    }

    [Fact]
    public void Resolve_RoundsPhysicalOffset_AwayFromZero_AtExactHalf()
    {
        // offsetX=1.0 論理px、dpi=48（50%）: 1.0*48/96=0.5 という厳密な中間値になる。
        // 0.5 は 2 進数で厳密に表現できるため、丸め誤差の懸念なく AwayFromZero（→1。ToEven なら→0）を検証できる。
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var monitor = Mon(work: work, isPrimary: true, dpi: 48, devicePath: "MON-1");
        var position = new BackgroundImagePosition("MON-1", BackgroundImageAnchor.TopLeft, 1.0, 0.0);

        var result = BackgroundImagePlacement.Resolve(new[] { monitor }, position, 128, 16);

        Assert.NotNull(result);
        Assert.Equal(new Rectangle(1, 0, 64, 64), result!.Rect);
    }

    [Fact]
    public void Resolve_MonitorDpiZero_TreatedAs96()
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var monitorZero = Mon(work: work, isPrimary: true, dpi: 0, devicePath: "MON-1");
        var monitor96 = Mon(work: work, isPrimary: true, dpi: 96, devicePath: "MON-1");
        var position = new BackgroundImagePosition("MON-1", BackgroundImageAnchor.TopLeft, 24.0, 16.0);

        var resultZero = BackgroundImagePlacement.Resolve(new[] { monitorZero }, position, 128, 16);
        var result96 = BackgroundImagePlacement.Resolve(new[] { monitor96 }, position, 128, 16);

        Assert.NotNull(resultZero);
        Assert.NotNull(result96);
        Assert.Equal(result96!.Rect, resultZero!.Rect);
    }

    [Fact]
    public void Resolve_PreservesOffsetFromAnchorCorner_AcrossDifferentWorkAreaSizes()
    {
        // BottomRight 基準: 右端・下端からの距離は作業領域の大きさ（解像度）によらず一定であること。
        var position = new BackgroundImagePosition("MON-1", BackgroundImageAnchor.BottomRight, 24.0, 16.0);

        var small = Mon(work: Rectangle.FromLTRB(0, 0, 1280, 720), isPrimary: true, dpi: 96, devicePath: "MON-1");
        var large = Mon(work: Rectangle.FromLTRB(0, 0, 3840, 2160), isPrimary: true, dpi: 96, devicePath: "MON-1");

        var resultSmall = BackgroundImagePlacement.Resolve(new[] { small }, position, 128, 16);
        var resultLarge = BackgroundImagePlacement.Resolve(new[] { large }, position, 128, 16);

        Assert.NotNull(resultSmall);
        Assert.NotNull(resultLarge);

        var distRightSmall = small.WorkRect.Right - resultSmall!.Rect.Right;
        var distBottomSmall = small.WorkRect.Bottom - resultSmall.Rect.Bottom;
        var distRightLarge = large.WorkRect.Right - resultLarge!.Rect.Right;
        var distBottomLarge = large.WorkRect.Bottom - resultLarge.Rect.Bottom;

        Assert.Equal(24, distRightSmall);
        Assert.Equal(16, distBottomSmall);
        Assert.Equal(distRightSmall, distRightLarge);
        Assert.Equal(distBottomSmall, distBottomLarge);
    }

    // ============================================================
    // Resolve: I-3（モニター不一致時のフォールバック）、大小無視の一致
    // ============================================================

    [Fact]
    public void Resolve_UnknownMonitorId_FallsBackToPrimary()
    {
        var primary = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, devicePath: "PRIMARY-ID");
        var other = Mon(work: Rectangle.FromLTRB(1920, 0, 3840, 1080), isPrimary: false, devicePath: "OTHER-ID");
        var position = new BackgroundImagePosition("NON-EXISTENT-ID", BackgroundImageAnchor.TopLeft, 24.0, 16.0);

        var result = BackgroundImagePlacement.Resolve(new[] { other, primary }, position, 128, 16);

        Assert.NotNull(result);
        Assert.Equal(primary, result!.Monitor);
        Assert.True(result.UsedPrimaryFallback);
    }

    [Fact]
    public void Resolve_EmptyMonitorId_FallsBackToPrimary()
    {
        var primary = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, devicePath: "PRIMARY-ID");
        var position = new BackgroundImagePosition("", BackgroundImageAnchor.TopLeft, 24.0, 16.0);

        var result = BackgroundImagePlacement.Resolve(new[] { primary }, position, 128, 16);

        Assert.NotNull(result);
        Assert.Equal(primary, result!.Monitor);
        Assert.True(result.UsedPrimaryFallback);
    }

    [Fact]
    public void Resolve_MonitorIdMatch_IsCaseInsensitive()
    {
        var primary = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, devicePath: "Primary-ID");
        var target = Mon(work: Rectangle.FromLTRB(1920, 0, 3840, 1080), isPrimary: false, devicePath: "TARGET-ID");
        var position = new BackgroundImagePosition("target-id", BackgroundImageAnchor.TopLeft, 0.0, 0.0);

        var result = BackgroundImagePlacement.Resolve(new[] { primary, target }, position, 128, 16);

        Assert.NotNull(result);
        Assert.Equal(target, result!.Monitor);
        Assert.False(result.UsedPrimaryFallback);
    }

    // ============================================================
    // Resolve: I-2（結果の矩形は Monitor.WorkRect に完全に含まれる）
    // ============================================================

    [Fact]
    public void Resolve_LargeOffset_ClampsWithinWorkRect()
    {
        var monitor = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, devicePath: "MON-1");
        var position = new BackgroundImagePosition("MON-1", BackgroundImageAnchor.BottomRight, 100000.0, 100000.0);

        var result = BackgroundImagePlacement.Resolve(new[] { monitor }, position, 128, 16);

        Assert.NotNull(result);
        AssertWithin(result!.Rect, monitor.WorkRect);
    }

    // ============================================================
    // SizeFor
    // ============================================================

    [Theory]
    [InlineData(96u, 128, 128)]  // 100%: そのまま
    [InlineData(192u, 128, 256)] // 200%: 2倍
    [InlineData(0u, 128, 128)]   // DPI=0 は 96 として扱う
    public void SizeFor_ScalesWithDpi_OnLargeWorkArea(uint dpi, int logicalSize, int expected)
    {
        var monitor = Mon(work: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, dpi: dpi);

        var actual = BackgroundImagePlacement.SizeFor(monitor, logicalSize);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SizeFor_ShrinksToFitSmallWorkArea()
    {
        // SizeFor は BackgroundImageLayout.Compute と異なり余白を引かないため、
        // 短辺(60)がそのまま上限になる。
        var monitor = Mon(work: Rectangle.FromLTRB(0, 0, 100, 60), isPrimary: true, dpi: 96);

        var actual = BackgroundImagePlacement.SizeFor(monitor, 128);

        Assert.Equal(60, actual);
    }

    [Fact]
    public void SizeFor_NeverGoesBelowOne()
    {
        var monitor = Mon(work: Rectangle.FromLTRB(0, 0, 0, 0), isPrimary: true, dpi: 96);

        var actual = BackgroundImagePlacement.SizeFor(monitor, 128);

        Assert.Equal(1, actual);
    }

    // ============================================================
    // SelectMonitor
    // ============================================================

    [Fact]
    public void SelectMonitor_PicksLargestOverlap()
    {
        var m1 = Mon(monitorRect: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, deviceName: "M1");
        var m2 = Mon(monitorRect: Rectangle.FromLTRB(1920, 0, 3840, 1080), isPrimary: false, deviceName: "M2");
        var m3 = Mon(monitorRect: Rectangle.FromLTRB(3840, 0, 5760, 1080), isPrimary: false, deviceName: "M3");
        var rect = Rectangle.FromLTRB(1800, 0, 2800, 1000); // m1 と 120x1000、m2 と 880x1000 重なる

        var selected = BackgroundImagePlacement.SelectMonitor(new[] { m1, m2, m3 }, rect);

        Assert.Equal(m2, selected);
    }

    [Fact]
    public void SelectMonitor_TiedOverlapArea_PrefersPrimary()
    {
        var m1 = Mon(monitorRect: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, deviceName: "M1");
        var m2 = Mon(monitorRect: Rectangle.FromLTRB(1920, 0, 3840, 1080), isPrimary: false, deviceName: "M2");
        var rect = Rectangle.FromLTRB(1720, 0, 2120, 1000); // 両モニターに 200x1000 ずつ対称に重なる

        // 列挙順は非プライマリの m2 を先に置き、「プライマリ優先」が「列挙順優先」の副作用でないことを検証する。
        var selected = BackgroundImagePlacement.SelectMonitor(new[] { m2, m1 }, rect);

        Assert.Equal(m1, selected);
    }

    [Fact]
    public void SelectMonitor_TiedOverlapArea_NoPrimary_PrefersFirstInEnumerationOrder()
    {
        var mA = Mon(monitorRect: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: false, deviceName: "A");
        var mB = Mon(monitorRect: Rectangle.FromLTRB(1920, 0, 3840, 1080), isPrimary: false, deviceName: "B");
        var rect = Rectangle.FromLTRB(1720, 0, 2120, 1000);

        var selected = BackgroundImagePlacement.SelectMonitor(new[] { mA, mB }, rect);

        Assert.Equal(mA, selected);
    }

    [Fact]
    public void SelectMonitor_NoIntersection_PicksNearest()
    {
        var m1 = Mon(monitorRect: Rectangle.FromLTRB(0, 0, 1920, 1080), isPrimary: true, deviceName: "M1");
        var m2 = Mon(monitorRect: Rectangle.FromLTRB(3000, 0, 4920, 1080), isPrimary: false, deviceName: "M2");
        var rect = Rectangle.FromLTRB(2000, 2000, 2100, 2100); // どちらとも重ならない

        var selected = BackgroundImagePlacement.SelectMonitor(new[] { m1, m2 }, rect);

        Assert.Equal(m1, selected);
    }

    [Fact]
    public void SelectMonitor_NoIntersection_TiedDistance_PrefersPrimary()
    {
        var m1 = Mon(monitorRect: Rectangle.FromLTRB(-2000, 0, -100, 1000), isPrimary: true, deviceName: "M1");
        var m2 = Mon(monitorRect: Rectangle.FromLTRB(100, 0, 2000, 1000), isPrimary: false, deviceName: "M2");
        var rect = Rectangle.FromLTRB(-50, 2000, 50, 2100); // 中心 x=0 が m1・m2 の両方から等距離

        var selected = BackgroundImagePlacement.SelectMonitor(new[] { m2, m1 }, rect);

        Assert.Equal(m1, selected);
    }

    [Fact]
    public void SelectMonitor_EmptyMonitors_ReturnsNull()
    {
        Assert.Null(BackgroundImagePlacement.SelectMonitor(Array.Empty<MonitorInfo>(), new Rectangle(0, 0, 128, 128)));
    }

    // ============================================================
    // Commit: I-4（結果の矩形は Monitor.WorkRect に含まれる）、IdentityKey、I-6
    // ============================================================

    [Fact]
    public void Commit_TopLeftDrop_ProducesExpectedPositionAndIdentityKeyFallback()
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        // DevicePath が空のときは DeviceName が IdentityKey になることを併せて確認する。
        var monitor = Mon(monitorRect: work, work: work, isPrimary: true, dpi: 96, deviceName: "DISPLAY1", devicePath: "");
        var dropRect = new Rectangle(100, 50, 128, 128);

        var result = BackgroundImagePlacement.Commit(new[] { monitor }, dropRect);

        Assert.NotNull(result);
        Assert.Equal(new Rectangle(100, 50, 128, 128), result!.Rect);
        Assert.Equal("DISPLAY1", result.Position.MonitorId);
        Assert.Equal(BackgroundImageAnchor.TopLeft, result.Position.Anchor);
        Assert.Equal(100.0, result.Position.OffsetX);
        Assert.Equal(50.0, result.Position.OffsetY);
        Assert.True(result.Position.OffsetX >= 0);
        Assert.True(result.Position.OffsetY >= 0);
        AssertWithin(result.Rect, monitor.WorkRect);
    }

    [Fact]
    public void Commit_UsesDevicePath_OverDeviceName_WhenBothPresent()
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var monitor = Mon(monitorRect: work, work: work, isPrimary: true, deviceName: "\\\\.\\DISPLAY1", devicePath: "DEVICE-PATH-ID");
        var dropRect = new Rectangle(0, 0, 128, 128);

        var result = BackgroundImagePlacement.Commit(new[] { monitor }, dropRect);

        Assert.NotNull(result);
        Assert.Equal("DEVICE-PATH-ID", result!.Position.MonitorId);
    }

    [Fact]
    public void Commit_CenterTie_ResolvesToTopRight()
    {
        // x+size/2、y+size/2 とも work の中心にちょうど一致するケース。
        // 契約どおり「中心ちょうどのときは right・top」になること（isLeft は <、isTop は <=）。
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var monitor = Mon(monitorRect: work, work: work, isPrimary: true, devicePath: "MON-1");
        var dropRect = new Rectangle(896, 476, 128, 128);

        var result = BackgroundImagePlacement.Commit(new[] { monitor }, dropRect);

        Assert.NotNull(result);
        Assert.Equal(BackgroundImageAnchor.TopRight, result!.Position.Anchor);
    }

    [Theory]
    [InlineData(100, 50, BackgroundImageAnchor.TopLeft, 100.0, 50.0)]
    [InlineData(1700, 50, BackgroundImageAnchor.TopRight, 92.0, 50.0)]
    [InlineData(100, 900, BackgroundImageAnchor.BottomLeft, 100.0, 52.0)]
    [InlineData(1700, 900, BackgroundImageAnchor.BottomRight, 92.0, 52.0)]
    public void Commit_DetectsCorrectCornerAndOffsets(int x, int y, BackgroundImageAnchor expectedAnchor, double expOffX, double expOffY)
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var monitor = Mon(monitorRect: work, work: work, isPrimary: true, devicePath: "MON-1");
        var dropRect = new Rectangle(x, y, 128, 128);

        var result = BackgroundImagePlacement.Commit(new[] { monitor }, dropRect);

        Assert.NotNull(result);
        Assert.Equal(new Rectangle(x, y, 128, 128), result!.Rect);
        Assert.Equal(expectedAnchor, result.Position.Anchor);
        Assert.Equal(expOffX, result.Position.OffsetX);
        Assert.Equal(expOffY, result.Position.OffsetY);
        Assert.True(result.Position.OffsetX >= 0);
        Assert.True(result.Position.OffsetY >= 0);
    }

    [Fact]
    public void Commit_DropRectLargerThanWorkArea_ShrinksToFit()
    {
        var work = Rectangle.FromLTRB(0, 0, 100, 60);
        var monitor = Mon(monitorRect: work, work: work, isPrimary: true, devicePath: "MON-1");
        var dropRect = new Rectangle(10, 10, 200, 200); // 正方形だが work よりずっと大きい

        var result = BackgroundImagePlacement.Commit(new[] { monitor }, dropRect);

        Assert.NotNull(result);
        Assert.Equal(60, result!.Rect.Width);  // min(200, min(100,60)) = 60
        Assert.Equal(60, result.Rect.Height);
        AssertWithin(result.Rect, work);
    }

    [Fact]
    public void Commit_EmptyMonitors_ReturnsNull()
    {
        Assert.Null(BackgroundImagePlacement.Commit(Array.Empty<MonitorInfo>(), new Rectangle(0, 0, 128, 128)));
    }

    // ============================================================
    // 往復一致（I-5）: dropRect.Width == SizeFor(Commit結果.Monitor, logicalSize) のとき、
    // Resolve(monitors, Commit結果.Position, logicalSize, margin).Rect == Commit結果.Rect
    // ============================================================

    [Theory]
    [InlineData(96u)]  // 100%
    [InlineData(120u)] // 125%
    [InlineData(144u)] // 150%
    [InlineData(168u)] // 175%
    [InlineData(192u)] // 200%
    [InlineData(384u)] // 400%
    public void CommitThenResolve_RoundTrips_ToSameRect(uint dpi)
    {
        var work = Rectangle.FromLTRB(0, 0, 3840, 2160); // 400% でも size が縮まない大きさ
        var monitor = Mon(monitorRect: work, work: work, isPrimary: true, dpi: dpi, devicePath: "MON-1");
        var monitors = new[] { monitor };

        var size = BackgroundImagePlacement.SizeFor(monitor, 128);
        var dropRect = new Rectangle(300, 200, size, size); // dropRect.Width == SizeFor(...)（往復一致の前提）

        var commitResult = BackgroundImagePlacement.Commit(monitors, dropRect);
        Assert.NotNull(commitResult);

        var resolveResult = BackgroundImagePlacement.Resolve(monitors, commitResult!.Position, 128, 16);

        Assert.NotNull(resolveResult);
        Assert.Equal(commitResult.Rect, resolveResult!.Rect);
    }

    [Fact]
    public void CommitThenResolve_PreservesOffsetFromCorner_AcrossDifferentResolutions()
    {
        // 同じドロップ位置(隅からの距離)を、解像度(作業領域)が異なる2つのモニターで確定・解決し、
        // 隅からの物理距離が一致することを確認する。
        var workSmall = Rectangle.FromLTRB(0, 0, 1280, 720);
        var workLarge = Rectangle.FromLTRB(0, 0, 3840, 2160);
        var monitorSmall = Mon(monitorRect: workSmall, work: workSmall, isPrimary: true, dpi: 96, devicePath: "MON-1");
        var monitorLarge = Mon(monitorRect: workLarge, work: workLarge, isPrimary: true, dpi: 96, devicePath: "MON-1");

        var dropRect = new Rectangle(40, 30, 128, 128); // 左上隅から (40,30) の同じ位置

        var commitSmall = BackgroundImagePlacement.Commit(new[] { monitorSmall }, dropRect);
        var commitLarge = BackgroundImagePlacement.Commit(new[] { monitorLarge }, dropRect);

        Assert.NotNull(commitSmall);
        Assert.NotNull(commitLarge);
        Assert.Equal(commitSmall!.Position.OffsetX, commitLarge!.Position.OffsetX);
        Assert.Equal(commitSmall.Position.OffsetY, commitLarge.Position.OffsetY);
        Assert.Equal(commitSmall.Position.Anchor, commitLarge.Position.Anchor);
    }

    // ============================================================
    // ドラッグ補助
    // ============================================================

    [Theory]
    [InlineData(5, 0, 5, 5, false)]  // X がしきい値ちょうど → false
    [InlineData(0, 5, 5, 5, false)]  // Y がしきい値ちょうど → false
    [InlineData(6, 0, 5, 5, true)]   // X がしきい値を超える → true
    [InlineData(0, 6, 5, 5, true)]   // Y がしきい値を超える → true
    [InlineData(-6, 0, 5, 5, true)]  // 負方向に超えても true
    public void ExceedsDragThreshold_OnlyTrueWhenStrictlyExceeded(int dx, int dy, int cxDrag, int cyDrag, bool expected)
    {
        var start = new Point(100, 100);
        var current = new Point(100 + dx, 100 + dy);

        var actual = BackgroundImagePlacement.ExceedsDragThreshold(start, current, cxDrag, cyDrag);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FollowPointer_TranslatesByPointerDelta()
    {
        var startRect = new Rectangle(50, 60, 128, 128);
        var startCursor = new Point(200, 210);
        var cursor = new Point(230, 190); // dx=+30, dy=-20

        var result = BackgroundImagePlacement.FollowPointer(startRect, startCursor, cursor);

        Assert.Equal(new Rectangle(80, 40, 128, 128), result);
    }

    [Fact]
    public void RescaleAroundGrabPoint_KeepsRelativeGrabPointRatio_RoundingAwayFromZero()
    {
        // rx=1/8=0.125, ry=2/8=0.25 は 2 進数で厳密に表現できる分数にして、
        // 0.125*20=2.5 という厳密な中間値で AwayFromZero（→3。ToEven なら→2）を確認する。
        var rect = new Rectangle(100, 100, 8, 8);
        var cursor = new Point(101, 102);

        var result = BackgroundImagePlacement.RescaleAroundGrabPoint(rect, cursor, 20);

        Assert.Equal(new Rectangle(98, 97, 20, 20), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]           // rect より左上 → 比率 0 にクランプ
    [InlineData(500, 500, 460, 460)]   // rect より右下 → 比率 1 にクランプ
    public void RescaleAroundGrabPoint_ClampsRatioTo0And1_WhenCursorOutsideRect(int cursorX, int cursorY, int expX, int expY)
    {
        var rect = new Rectangle(100, 100, 50, 50);
        var cursor = new Point(cursorX, cursorY);

        var result = BackgroundImagePlacement.RescaleAroundGrabPoint(rect, cursor, 40);

        Assert.Equal(new Rectangle(expX, expY, 40, 40), result);
    }

    [Fact]
    public void RescaleAroundGrabPoint_ZeroSizedRect_DoesNotDivideByZero()
    {
        var rect = new Rectangle(100, 100, 0, 0);
        var cursor = new Point(150, 150);

        var result = BackgroundImagePlacement.RescaleAroundGrabPoint(rect, cursor, 40);

        // rx = ry = 0 として扱われ、cursor がそのまま新しい矩形の左上になる。
        Assert.Equal(new Rectangle(150, 150, 40, 40), result);
    }

    // ============================================================
    // monitors が空のときは null（Resolve は上の Theory 群で別途カバー済みではないため個別に確認）
    // ============================================================

    [Fact]
    public void Resolve_EmptyMonitors_ReturnsNull()
    {
        Assert.Null(BackgroundImagePlacement.Resolve(Array.Empty<MonitorInfo>(), null, 128, 16));
    }

    // ============================================================
    // テスト用ヘルパー
    // ============================================================

    // テスト用 MonitorInfo を簡潔に組み立てるヘルパー。monitorRect/work のどちらか一方だけ指定した場合は、
    // もう片方にも同じ値を使う（SelectMonitor は MonitorRect を、Resolve/Commit は WorkRect を見るため、
    // テストの主眼に応じて省略できるようにする）。
    private static MonitorInfo Mon(
        Rectangle? monitorRect = null,
        Rectangle? work = null,
        bool isPrimary = false,
        uint dpi = 96,
        string deviceName = "",
        string devicePath = "")
    {
        var m = monitorRect ?? work ?? Rectangle.Empty;
        var w = work ?? monitorRect ?? Rectangle.Empty;
        return new MonitorInfo(m, w, isPrimary, dpi, dpi, deviceName, devicePath);
    }

    // 矩形 inner が outer に完全に収まっているか（BackgroundImageLayoutTests.AssertWithin と同じ）。
    private static void AssertWithin(Rectangle inner, Rectangle outer)
    {
        Assert.True(inner.Left >= outer.Left, $"inner.Left({inner.Left}) < outer.Left({outer.Left})");
        Assert.True(inner.Top >= outer.Top, $"inner.Top({inner.Top}) < outer.Top({outer.Top})");
        Assert.True(inner.Right <= outer.Right, $"inner.Right({inner.Right}) > outer.Right({outer.Right})");
        Assert.True(inner.Bottom <= outer.Bottom, $"inner.Bottom({inner.Bottom}) > outer.Bottom({outer.Bottom})");
    }
}
