// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// 背景画像の配置に関わる計算をまとめた、Win32 を呼ばない純関数群（値オブジェクト、T014）。
/// 契約: specs/018-draggable-background-image/contracts/background-image-placement-contract.md
/// （根拠: research.md R-4〜R-7）。対象は次の 3 つ。
/// <list type="bullet">
/// <item><description>記憶位置からの表示矩形の解決（<see cref="Resolve"/>）</description></item>
/// <item><description>ドロップ位置の確定（<see cref="Commit"/>）</description></item>
/// <item><description>重なり最大のモニターの選択（<see cref="SelectMonitor"/>）</description></item>
/// </list>
/// 既存の <see cref="BackgroundImageLayout"/>（右上の既定矩形の計算）と対になるクラスで、未移動のとき
/// （<c>position == null</c>）は <see cref="BackgroundImageLayout.Compute"/> をそのまま呼ぶ。座標はすべて
/// 物理 px・仮想スクリーン座標、位置の記憶値（<see cref="BackgroundImagePosition"/>）は論理 px とする。
/// </summary>
public static class BackgroundImagePlacement
{
    // 論理 px の基準 DPI（100%）。BackgroundImageLayout.BaseDpi と同じ値（契約の「共通の定義」節）。
    private const int BaseDpi = 96;

    /// <summary>
    /// <see cref="Resolve"/> の結果。
    /// </summary>
    /// <param name="Rect">表示矩形（物理 px、仮想スクリーン座標）。</param>
    /// <param name="Monitor">表示先として選ばれたモニター。</param>
    /// <param name="UsedPrimaryFallback">
    /// true の場合、<c>position.MonitorId</c> に一致するモニターが見つからずプライマリへフォールバックした
    /// （この場合も <c>position</c> 自体は書き換えない。FR-019）。
    /// </param>
    public sealed record PlacementResult(Rectangle Rect, MonitorInfo Monitor, bool UsedPrimaryFallback);

    /// <summary>
    /// <see cref="Commit"/> の結果。
    /// </summary>
    /// <param name="Rect">作業領域へ寄せた後の表示矩形（物理 px、仮想スクリーン座標）。</param>
    /// <param name="Monitor">ドロップ先として選ばれたモニター。</param>
    /// <param name="Position">保存すべき新しい位置（論理 px）。</param>
    public sealed record CommitResult(Rectangle Rect, MonitorInfo Monitor, BackgroundImagePosition Position);

    /// <summary>
    /// 記憶位置（<paramref name="position"/>）から表示矩形を解決する。<paramref name="position"/> が null
    /// のときは <see cref="BackgroundImageLayout.Compute"/> による右上の既定位置を返す（<c>position</c> は
    /// 読むだけで変更しない）。
    /// </summary>
    /// <param name="monitors">現在のモニター一覧（列挙順を維持すること）。</param>
    /// <param name="position">記憶されている位置。未移動なら null。</param>
    /// <param name="logicalSize">一辺の論理 px。</param>
    /// <param name="defaultLogicalMargin">
    /// <paramref name="position"/> が null のときに <see cref="BackgroundImageLayout.Compute"/> へ渡す、
    /// 作業領域の右端・上端からの余白（論理 px）。
    /// </param>
    /// <returns><paramref name="monitors"/> が空なら null。</returns>
    public static PlacementResult? Resolve(
        IReadOnlyList<MonitorInfo> monitors,
        BackgroundImagePosition? position,
        int logicalSize,
        int defaultLogicalMargin)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        var primary = Primary(monitors);

        if (position is null)
        {
            var defaultRect = BackgroundImageLayout.Compute(primary.WorkRect, primary.DpiX, logicalSize, defaultLogicalMargin);
            return new PlacementResult(defaultRect, primary, UsedPrimaryFallback: false);
        }

        MonitorInfo? matched = null;
        foreach (var monitor in monitors)
        {
            if (KeyMatches(monitor, position.MonitorId))
            {
                matched = monitor;
                break;
            }
        }

        var target = matched ?? primary;
        var work = target.WorkRect;
        var size = SizeFor(target, logicalSize);
        var ox = ToPhysical(position.OffsetX, target);
        var oy = ToPhysical(position.OffsetY, target);

        var x = position.Anchor is BackgroundImageAnchor.TopLeft or BackgroundImageAnchor.BottomLeft
            ? work.Left + ox
            : work.Right - ox - size;
        var y = position.Anchor is BackgroundImageAnchor.TopLeft or BackgroundImageAnchor.TopRight
            ? work.Top + oy
            : work.Bottom - oy - size;

        x = Math.Clamp(x, work.Left, work.Right - size);
        y = Math.Clamp(y, work.Top, work.Bottom - size);

        return new PlacementResult(new Rectangle(x, y, size, size), target, UsedPrimaryFallback: matched is null);
    }

    /// <summary>
    /// ドロップ位置を確定する。作業領域へ寄せたうえで、基準の隅と距離を算出する。
    /// </summary>
    /// <param name="monitors">現在のモニター一覧。</param>
    /// <param name="dropRect">ドロップされた瞬間の矩形（物理 px、仮想スクリーン座標）。正方形を前提とする。</param>
    /// <returns><paramref name="monitors"/> が空なら null。</returns>
    public static CommitResult? Commit(IReadOnlyList<MonitorInfo> monitors, Rectangle dropRect)
    {
        var target = SelectMonitor(monitors, dropRect);
        if (target is null)
        {
            return null;
        }

        var work = target.WorkRect;
        var size = Math.Max(1, Math.Min(dropRect.Width, Math.Min(work.Width, work.Height)));

        var x = Math.Clamp(dropRect.X, work.Left, work.Right - size);
        var y = Math.Clamp(dropRect.Y, work.Top, work.Bottom - size);

        // 中心ちょうどのときは right・top になる（isLeft は <、isTop は <=）。
        var isLeft = x + size / 2.0 < work.Left + work.Width / 2.0;
        var isTop = y + size / 2.0 <= work.Top + work.Height / 2.0;
        var anchor = isTop
            ? (isLeft ? BackgroundImageAnchor.TopLeft : BackgroundImageAnchor.TopRight)
            : (isLeft ? BackgroundImageAnchor.BottomLeft : BackgroundImageAnchor.BottomRight);

        var offXpx = isLeft ? x - work.Left : work.Right - (x + size);
        var offYpx = isTop ? y - work.Top : work.Bottom - (y + size);

        var position = new BackgroundImagePosition(
            target.IdentityKey,
            anchor,
            ToLogical(offXpx, target),
            ToLogical(offYpx, target));

        return new CommitResult(new Rectangle(x, y, size, size), target, position);
    }

    /// <summary>
    /// <paramref name="rect"/> と重なりが最大のモニターを選ぶ。重なりが無ければ、<paramref name="rect"/> の
    /// 中心に最も近いモニターを選ぶ（<c>MonitorFromRect(MONITOR_DEFAULTTONEAREST)</c> と同じ考え方）。
    /// 同点のときはプライマリを優先し、次に <paramref name="monitors"/> の列挙順が早いものを優先する。
    /// </summary>
    /// <param name="monitors">現在のモニター一覧。</param>
    /// <param name="rect">
    /// 選択の基準にする矩形（物理 px、仮想スクリーン座標）。<see cref="MonitorInfo.MonitorRect"/> と比較する
    /// （寄せには使わない。寄せは <see cref="MonitorInfo.WorkRect"/> を使う）。
    /// </param>
    /// <returns><paramref name="monitors"/> が空なら null。</returns>
    public static MonitorInfo? SelectMonitor(IReadOnlyList<MonitorInfo> monitors, Rectangle rect)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        MonitorInfo? bestOverlap = null;
        var bestOverlapArea = 0L;

        foreach (var monitor in monitors)
        {
            var intersection = Rectangle.Intersect(monitor.MonitorRect, rect);
            var area = (long)intersection.Width * intersection.Height;
            if (area <= 0)
            {
                continue;
            }

            if (bestOverlap is null || area > bestOverlapArea || (area == bestOverlapArea && IsPreferredOver(monitor, bestOverlap)))
            {
                bestOverlap = monitor;
                bestOverlapArea = area;
            }
        }

        if (bestOverlap is not null)
        {
            return bestOverlap;
        }

        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;

        MonitorInfo? nearest = null;
        var nearestDistanceSquared = double.MaxValue;

        foreach (var monitor in monitors)
        {
            var distanceSquared = DistanceSquaredToRect(centerX, centerY, monitor.MonitorRect);
            if (nearest is null || distanceSquared < nearestDistanceSquared || (distanceSquared == nearestDistanceSquared && IsPreferredOver(monitor, nearest)))
            {
                nearest = monitor;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 指定モニターにおける表示サイズ（物理 px）を返す。DPI で拡縮したうえで、作業領域の短辺を超えないよう
    /// 縮める（<see cref="BackgroundImageLayout.Compute"/>と異なり、余白は引かない）。
    /// </summary>
    /// <param name="monitor">対象モニター。</param>
    /// <param name="logicalSize">一辺の論理 px。</param>
    public static int SizeFor(MonitorInfo monitor, int logicalSize)
    {
        var scaled = ScaledSize(logicalSize, EffectiveDpi(monitor));
        var maxFit = Math.Min(monitor.WorkRect.Width, monitor.WorkRect.Height);
        return Math.Max(1, Math.Min(scaled, maxFit));
    }

    /// <summary>
    /// カーソルの移動量がドラッグ開始のしきい値（<c>GetSystemMetrics(SM_CXDRAG / SM_CYDRAG)</c>）を
    /// 超えたかどうか。
    /// </summary>
    public static bool ExceedsDragThreshold(Point start, Point current, int cxDrag, int cyDrag)
        => Math.Abs(current.X - start.X) > cxDrag || Math.Abs(current.Y - start.Y) > cyDrag;

    /// <summary>
    /// <paramref name="startRect"/> を、カーソルの移動量（<paramref name="startCursor"/> から
    /// <paramref name="cursor"/> まで）だけ平行移動した矩形を返す（サイズは変えない）。
    /// </summary>
    public static Rectangle FollowPointer(Rectangle startRect, Point startCursor, Point cursor)
    {
        var dx = cursor.X - startCursor.X;
        var dy = cursor.Y - startCursor.Y;
        return new Rectangle(startRect.X + dx, startRect.Y + dy, startRect.Width, startRect.Height);
    }

    /// <summary>
    /// <paramref name="cursor"/> が <paramref name="rect"/> 内で占める相対位置（0〜1 にクランプ）を保ったまま、
    /// 一辺 <paramref name="newSize"/> の矩形に拡縮する。DPI の異なるモニターをまたいで大きさが変わったときに、
    /// つかんだ点が画像内の同じ相対位置に来るよう位置を補正する（research.md R-6）。
    /// </summary>
    public static Rectangle RescaleAroundGrabPoint(Rectangle rect, Point cursor, int newSize)
    {
        // rect.Width / rect.Height が 0 の場合の 0 除算を避け、rx/ry は 0 として扱う。
        var rx = rect.Width == 0 ? 0.0 : Math.Clamp((cursor.X - rect.X) / (double)rect.Width, 0.0, 1.0);
        var ry = rect.Height == 0 ? 0.0 : Math.Clamp((cursor.Y - rect.Y) / (double)rect.Height, 0.0, 1.0);

        var x = cursor.X - (int)Math.Round(rx * newSize, MidpointRounding.AwayFromZero);
        var y = cursor.Y - (int)Math.Round(ry * newSize, MidpointRounding.AwayFromZero);

        return new Rectangle(x, y, newSize, newSize);
    }

    // ==== 共通の定義（契約書「共通の定義」節） ====

    private static int EffectiveDpi(MonitorInfo monitor) => monitor.DpiX == 0 ? BaseDpi : (int)monitor.DpiX;

    // BackgroundImageLayout.Compute と同じ丸め（共用、MulDiv は internal 化して直接呼ぶ）。
    private static int ScaledSize(int logicalValue, int dpi) => BackgroundImageLayout.MulDiv(logicalValue, dpi, BaseDpi);

    private static int ToPhysical(double offset, MonitorInfo monitor)
        => (int)Math.Round(offset * EffectiveDpi(monitor) / BaseDpi, MidpointRounding.AwayFromZero);

    private static double ToLogical(double px, MonitorInfo monitor)
        => Math.Round(px * BaseDpi / EffectiveDpi(monitor), 2, MidpointRounding.AwayFromZero);

    // IsPrimary が true の最初の要素。無ければ monitors[0]（DisplayHelper.GetPrimaryMonitor と同じ規則）。
    // 呼び出し側で monitors が空でないことを確認済みであること。
    private static MonitorInfo Primary(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }
        return monitors[0];
    }

    private static bool KeyMatches(MonitorInfo monitor, string id)
        => !string.IsNullOrEmpty(id) && string.Equals(monitor.IdentityKey, id, StringComparison.OrdinalIgnoreCase);

    // SelectMonitor の同点判定: プライマリを優先する。列挙順の早さは呼び出し側（同点で false のときは
    // 置き換えない = 先着を残す）で担保する。
    private static bool IsPreferredOver(MonitorInfo candidate, MonitorInfo currentBest)
        => candidate.IsPrimary && !currentBest.IsPrimary;

    // rect の中心から monitorRect までの最短距離の二乗（矩形の内側なら 0）。
    // X/Y それぞれ Clamp(center, min, max) した最近接点との距離で求める。
    private static double DistanceSquaredToRect(double centerX, double centerY, Rectangle monitorRect)
    {
        var closestX = Math.Clamp(centerX, monitorRect.Left, monitorRect.Right);
        var closestY = Math.Clamp(centerY, monitorRect.Top, monitorRect.Bottom);
        var dx = centerX - closestX;
        var dy = centerY - closestY;
        return dx * dx + dy * dy;
    }
}
