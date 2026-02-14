using System.Windows.Media;
using IMEIndicatorClock.Services;
using Xunit;
using Color = System.Windows.Media.Color;

namespace IMEIndicatorW.Tests;

/// <summary>
/// ColorHelperのユニットテスト
/// 純粋なロジックテスト（Win32 API依存なし）
/// </summary>
public class ColorHelperTests
{
    // ============================================================
    // ParseColor — #RRGGBB 形式
    // ============================================================

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    [InlineData("#0000FF", 0, 0, 255)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    public void ParseColor_RRGGBB_ReturnsCorrectColor(string hex, byte r, byte g, byte b)
    {
        var color = ColorHelper.ParseColor(hex);

        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    // ============================================================
    // ParseColor — #RGB 形式
    // ============================================================

    [Theory]
    [InlineData("#F00", 255, 0, 0)]
    [InlineData("#0F0", 0, 255, 0)]
    [InlineData("#00F", 0, 0, 255)]
    public void ParseColor_RGB_ReturnsCorrectColor(string hex, byte r, byte g, byte b)
    {
        var color = ColorHelper.ParseColor(hex);

        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    // ============================================================
    // ParseColor — #AARRGGBB 形式
    // ============================================================

    [Fact]
    public void ParseColor_AARRGGBB_SemiTransparentRed()
    {
        var color = ColorHelper.ParseColor("#80FF0000");

        Assert.Equal(0x80, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ParseColor_AARRGGBB_OpaqueWhite()
    {
        var color = ColorHelper.ParseColor("#FFFFFFFF");

        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(255, color.B);
    }

    // ============================================================
    // ParseColor — 無効な入力
    // ============================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#ZZZZZZ")]
    [InlineData("#12345")]
    public void ParseColor_Invalid_ReturnsGray(string? hex)
    {
        var color = ColorHelper.ParseColor(hex!);

        Assert.Equal(Colors.Gray, color);
    }

    // ============================================================
    // ToRgbHex / ToArgbHex
    // ============================================================

    [Fact]
    public void ToRgbHex_Black()
    {
        Assert.Equal("#000000", ColorHelper.ToRgbHex(Colors.Black));
    }

    [Fact]
    public void ToRgbHex_White()
    {
        Assert.Equal("#FFFFFF", ColorHelper.ToRgbHex(Colors.White));
    }

    [Fact]
    public void ToRgbHex_RoundTrip()
    {
        var original = Color.FromRgb(0x3B, 0x82, 0xF6);
        var hex = ColorHelper.ToRgbHex(original);
        var parsed = ColorHelper.ParseColor(hex);

        Assert.Equal(original.R, parsed.R);
        Assert.Equal(original.G, parsed.G);
        Assert.Equal(original.B, parsed.B);
    }

    [Fact]
    public void ToArgbHex_RoundTrip()
    {
        var original = Color.FromArgb(0x80, 0xFF, 0x00, 0x00);
        var hex = ColorHelper.ToArgbHex(original);
        var parsed = ColorHelper.ParseColor(hex);

        Assert.Equal(original.A, parsed.A);
        Assert.Equal(original.R, parsed.R);
        Assert.Equal(original.G, parsed.G);
        Assert.Equal(original.B, parsed.B);
    }

    // ============================================================
    // IsValidHexColor
    // ============================================================

    [Theory]
    [InlineData("#FF0000", true)]
    [InlineData("#F00", true)]
    [InlineData("#80FF0000", true)]
    [InlineData("#F00F", true)]
    [InlineData("#ZZZZZZ", false)]
    [InlineData("#12345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidHexColor_ReturnsExpected(string? hex, bool expected)
    {
        Assert.Equal(expected, ColorHelper.IsValidHexColor(hex!));
    }
}
