// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="BackgroundImageLayout"/>.Compute の単体テスト。
/// 移植元: tests/cpp/unit/BackgroundImageLayoutTests.cpp。
/// specs/013-ime-corner-image/data-model.md §3 の期待値表（7 ケース）に加え、
/// 短辺選択・既定引数の上書きを検証する。純関数（Win32 API 呼び出しなし）なので、
/// ディスプレイ構成に依存せず常に実行できる。
/// </summary>
public sealed class BackgroundImageLayoutTests
{
    // ---- 定数（FR-012: 128 論理 px 四方、余白 16 論理 px）----

    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(128, BackgroundImageLayout.DefaultLogicalSize);
        Assert.Equal(16, BackgroundImageLayout.DefaultLogicalMargin);
    }

    // ---- data-model.md §3 期待値表 + 短辺選択規則 ----

    [Theory]
    // workLeft, workTop, workRight, workBottom, dpi, expLeft, expTop, expRight, expBottom
    [InlineData(0, 0, 1920, 1080, 96u, 1776, 16, 1904, 144)]      // フル HD 100%
    [InlineData(0, 0, 1920, 1080, 144u, 1704, 24, 1896, 216)]     // フル HD 150%
    [InlineData(0, 0, 3840, 2160, 192u, 3552, 32, 3808, 288)]     // 4K 200%
    [InlineData(0, 48, 1920, 1080, 96u, 1776, 64, 1904, 192)]     // タスクバー上端
    [InlineData(0, 0, 1856, 1080, 96u, 1712, 16, 1840, 144)]      // タスクバー右端
    [InlineData(-2560, 0, 0, 1440, 96u, -144, 16, -16, 144)]      // 負座標のプライマリ
    [InlineData(0, 0, 100, 100, 96u, 16, 16, 84, 84)]             // 極小（両辺とも縮小）
    [InlineData(0, 0, 1920, 100, 96u, 1836, 16, 1904, 84)]        // 幅広だが低い（高さが基準）
    [InlineData(0, 0, 100, 1080, 96u, 16, 16, 84, 84)]            // 縦長だが狭い（幅が基準）
    [InlineData(0, 0, 20, 20, 96u, 3, 16, 4, 17)]                 // 余白より小さい → size=1 に固定
    public void Compute_MatchesDataModelExpectedRect(
        int workLeft, int workTop, int workRight, int workBottom, uint dpi,
        int expLeft, int expTop, int expRight, int expBottom)
    {
        var work = Rectangle.FromLTRB(workLeft, workTop, workRight, workBottom);
        var expected = Rectangle.FromLTRB(expLeft, expTop, expRight, expBottom);

        var actual = BackgroundImageLayout.Compute(work, dpi);

        Assert.Equal(expected, actual);
        AssertWithin(actual, work);
    }

    [Fact]
    public void Compute_DpiZero_TreatedAs96()
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);

        var atZero = BackgroundImageLayout.Compute(work, 0);
        var at96 = BackgroundImageLayout.Compute(work, 96);

        Assert.Equal(Rectangle.FromLTRB(1776, 16, 1904, 144), atZero);
        Assert.Equal(at96, atZero);
    }

    // ---- 既定引数の上書き ----

    [Theory]
    // dpi, logicalSize, logicalMargin, expLeft, expTop, expRight, expBottom
    [InlineData(96u, 64, 8, 1848, 8, 1912, 72)]     // 64 論理 px 四方、余白 8 @ 96 dpi
    [InlineData(192u, 64, 8, 1776, 16, 1904, 144)]  // 64/8 @ 192 dpi は 128/16 @ 96 dpi と同じ物理矩形
    public void Compute_CustomLogicalSizeAndMargin_AreHonoredAndScaleWithDpi(
        uint dpi, int logicalSize, int logicalMargin,
        int expLeft, int expTop, int expRight, int expBottom)
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var expected = Rectangle.FromLTRB(expLeft, expTop, expRight, expBottom);

        var actual = BackgroundImageLayout.Compute(work, dpi, logicalSize, logicalMargin);

        Assert.Equal(expected, actual);
    }

    // ---- 015-split-appearance-settings: 背景画像サイズ設定の値域境界（32〜512）----
    // Compute 自体は無改修（research.md R-1）だが、設定値として新たに導入する値域の両端が
    // 既存の作業領域クランプで正しく扱われることを確認する。

    [Theory]
    // logicalSize, expLeft, expTop, expRight, expBottom（work=FullHD 1920x1080、margin=16、dpi=96）
    [InlineData(32, 1872, 16, 1904, 48)]    // 値域下限
    [InlineData(512, 1392, 16, 1904, 528)]  // 値域上限（フル HD では縮小されない）
    public void Compute_HonorsNewSizeRangeBoundaries_OnTypicalDisplay(
        int logicalSize, int expLeft, int expTop, int expRight, int expBottom)
    {
        var work = Rectangle.FromLTRB(0, 0, 1920, 1080);
        var expected = Rectangle.FromLTRB(expLeft, expTop, expRight, expBottom);

        var actual = BackgroundImageLayout.Compute(work, 96, logicalSize);

        Assert.Equal(expected, actual);
        AssertWithin(actual, work);
    }

    [Fact]
    public void Compute_MaxSize512_ShrinksToFitOnSmallWorkArea()
    {
        // 低解像度ディスプレイ（例: 640x480）では 512 論理 px がそのまま収まらないため、
        // 既存の作業領域クランプ（avail = 短辺 - 2*margin）で縮小される必要がある（Edge Cases 節）。
        var work = Rectangle.FromLTRB(0, 0, 640, 480);

        var actual = BackgroundImageLayout.Compute(work, 96, 512);

        Assert.Equal(Rectangle.FromLTRB(176, 16, 624, 464), actual);
        AssertWithin(actual, work);
    }

    // 矩形 inner が outer に完全に収まっているか（FR-004: 作業領域の右上に配置、タスクバーと重ならない）。
    private static void AssertWithin(Rectangle inner, Rectangle outer)
    {
        Assert.True(inner.Left >= outer.Left, $"inner.Left({inner.Left}) < outer.Left({outer.Left})");
        Assert.True(inner.Top >= outer.Top, $"inner.Top({inner.Top}) < outer.Top({outer.Top})");
        Assert.True(inner.Right <= outer.Right, $"inner.Right({inner.Right}) > outer.Right({outer.Right})");
        Assert.True(inner.Bottom <= outer.Bottom, $"inner.Bottom({inner.Bottom}) > outer.Bottom({outer.Bottom})");
    }
}
