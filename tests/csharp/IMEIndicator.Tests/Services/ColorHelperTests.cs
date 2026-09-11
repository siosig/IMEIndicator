// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="ColorHelper"/> のテスト。現行 tests/cpp/unit/ColorHelperTests.cpp の移植。
/// C++ 版は内部表現が float（D2D1_COLOR_F、0.0〜1.0）のため比較に誤差許容（1/510）を使っていたが、
/// C# 版は System.Drawing.Color（byte、0〜255）を直接パースし丸め誤差が発生しないため、
/// 本ファイルでは全て厳密一致で検証する。
/// </summary>
public sealed class ColorHelperTests
{
    // 不正入力時のフォールバック色（ColorHelper.GrayFallback と同じ値）。
    private static readonly Color Gray = Color.FromArgb(255, 128, 128, 128);

    // ============================================================
    // ParseColor — #RRGGBB 形式
    // ============================================================

    [Theory]
    [InlineData("#FF0000", 255, 255, 0, 0)]
    [InlineData("#00FF00", 255, 0, 255, 0)]
    [InlineData("#0000FF", 255, 0, 0, 255)]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    [InlineData("#000000", 255, 0, 0, 0)]
    public void ParseColor_RRGGBB_MatchesExpectedArgb(string hex, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromArgb(a, r, g, b), ColorHelper.ParseColor(hex));
    }

    // ============================================================
    // ParseColor — #RGB 形式
    // ============================================================

    [Theory]
    [InlineData("#F00", 255, 255, 0, 0)]
    [InlineData("#0F0", 255, 0, 255, 0)]
    public void ParseColor_RGB_MatchesExpectedArgb(string hex, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromArgb(a, r, g, b), ColorHelper.ParseColor(hex));
    }

    // ============================================================
    // ParseColor — #AARRGGBB 形式
    // ============================================================

    [Fact]
    public void ParseColor_AARRGGBB_SemiTransparentRed()
    {
        var c = ColorHelper.ParseColor("#80FF0000");
        Assert.Equal(0x80, c.A);
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void ParseColor_AARRGGBB_OpaqueWhite()
    {
        Assert.Equal(Color.FromArgb(255, 255, 255, 255), ColorHelper.ParseColor("#FFFFFFFF"));
    }

    // ============================================================
    // ParseColor — 不正入力 → 灰色
    // ============================================================

    [Fact]
    public void ParseColor_Null_ReturnsGray()
    {
        // string? を受け取る InlineData(null) は params object[] の単一引数解決が曖昧になりうるため、
        // null ケースのみ専用の Fact に分離する。
        Assert.Equal(Gray, ColorHelper.ParseColor(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#ZZZZZZ")]
    [InlineData("#12345")]
    public void ParseColor_InvalidInput_ReturnsGray(string hex)
    {
        Assert.Equal(Gray, ColorHelper.ParseColor(hex));
    }

    // ============================================================
    // ToRgbHex / ToArgbHex
    // ============================================================

    [Fact]
    public void ToRgbHex_Black()
    {
        Assert.Equal("#000000", ColorHelper.ToRgbHex(Color.FromArgb(255, 0, 0, 0)));
    }

    [Fact]
    public void ToRgbHex_White()
    {
        Assert.Equal("#FFFFFF", ColorHelper.ToRgbHex(Color.FromArgb(255, 255, 255, 255)));
    }

    [Fact]
    public void ToRgbHex_RoundTrip()
    {
        var orig = ColorHelper.ParseColor("#3B82F6");
        var hex = ColorHelper.ToRgbHex(orig);
        var parsed = ColorHelper.ParseColor(hex);

        Assert.Equal("#3B82F6", hex);
        Assert.Equal(orig.R, parsed.R);
        Assert.Equal(orig.G, parsed.G);
        Assert.Equal(orig.B, parsed.B);
    }

    [Fact]
    public void ToArgbHex_RoundTrip()
    {
        var orig = ColorHelper.ParseColor("#80FF0000");
        var hex = ColorHelper.ToArgbHex(orig);
        var parsed = ColorHelper.ParseColor(hex);

        Assert.Equal("#80FF0000", hex);
        Assert.Equal(orig.A, parsed.A);
        Assert.Equal(orig.R, parsed.R);
        Assert.Equal(orig.G, parsed.G);
        Assert.Equal(orig.B, parsed.B);
    }

    // ============================================================
    // IsValidHexColor
    // ============================================================

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#F00")]
    [InlineData("#80FF0000")]
    [InlineData("#F00F")]
    [InlineData("FF0000")] // # 無しでも許容
    public void IsValidHexColor_Valid_ReturnsTrue(string hex)
    {
        Assert.True(ColorHelper.IsValidHexColor(hex));
    }

    [Fact]
    public void IsValidHexColor_Null_ReturnsFalse()
    {
        Assert.False(ColorHelper.IsValidHexColor(null));
    }

    [Theory]
    [InlineData("#ZZZZZZ")]
    [InlineData("#12345")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidHexColor_Invalid_ReturnsFalse(string hex)
    {
        Assert.False(ColorHelper.IsValidHexColor(hex));
    }
}
