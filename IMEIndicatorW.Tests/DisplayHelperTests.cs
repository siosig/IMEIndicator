using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// DisplayHelperのユニットテスト
/// Screen.AllScreensに依存するため、ディスプレイがない環境ではスキップ
/// </summary>
public class DisplayHelperTests
{
    private static bool HasScreens => System.Windows.Forms.Screen.AllScreens.Length > 0;

    [Fact]
    public void GetDisplayIndexFromPosition_PrimaryCenter_ReturnsZero()
    {
        if (!HasScreens) return;

        var primary = System.Windows.Forms.Screen.AllScreens[0].Bounds;
        double centerX = primary.Left + primary.Width / 2.0;
        double centerY = primary.Top + primary.Height / 2.0;

        var index = DisplayHelper.GetDisplayIndexFromPosition(
            centerX - 50, centerY - 50, 100, 100);

        Assert.Equal(0, index);
    }

    [Fact]
    public void IsValidDisplayIndex_Zero_ReturnsTrue()
    {
        if (!HasScreens) return;

        Assert.True(DisplayHelper.IsValidDisplayIndex(0));
    }

    [Fact]
    public void IsValidDisplayIndex_Negative_ReturnsFalse()
    {
        Assert.False(DisplayHelper.IsValidDisplayIndex(-1));
    }

    [Fact]
    public void IsValidDisplayIndex_TooLarge_ReturnsFalse()
    {
        Assert.False(DisplayHelper.IsValidDisplayIndex(9999));
    }

    [Fact]
    public void IsPositionOnAnyDisplay_PrimaryCenter_ReturnsTrue()
    {
        if (!HasScreens) return;

        var primary = System.Windows.Forms.Screen.AllScreens[0].Bounds;
        double centerX = primary.Left + primary.Width / 2.0;
        double centerY = primary.Top + primary.Height / 2.0;

        Assert.True(DisplayHelper.IsPositionOnAnyDisplay(centerX, centerY));
    }

    [Fact]
    public void IsPositionOnAnyDisplay_FarOffScreen_ReturnsFalse()
    {
        Assert.False(DisplayHelper.IsPositionOnAnyDisplay(-99999, -99999));
    }

    [Fact]
    public void GetValidPosition_ValidCoordinates_ReturnsOriginal()
    {
        if (!HasScreens) return;

        var primary = System.Windows.Forms.Screen.AllScreens[0].Bounds;
        double x = primary.Left + 100;
        double y = primary.Top + 100;

        var (rx, ry, displayIndex) = DisplayHelper.GetValidPosition(x, y, 100, 100, 0);

        Assert.Equal(x, rx);
        Assert.Equal(y, ry);
        Assert.True(displayIndex >= 0);
    }

    [Fact]
    public void GetValidPosition_InvalidCoordinates_ReturnsFallback()
    {
        if (!HasScreens) return;

        var (rx, ry, displayIndex) = DisplayHelper.GetValidPosition(
            -99999, -99999, 100, 100, 0);

        // フォールバック位置はディスプレイ内にある
        Assert.True(DisplayHelper.IsPositionOnAnyDisplay(rx + 50, ry + 50));
        Assert.Equal(0, displayIndex);
    }

    [Fact]
    public void GetValidPosition_InvalidDisplayIndex_FallsBackToZero()
    {
        if (!HasScreens) return;

        var (_, _, displayIndex) = DisplayHelper.GetValidPosition(
            -99999, -99999, 100, 100, 9999);

        Assert.Equal(0, displayIndex);
    }
}
