using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// WindowHandleStateManagerのユニットテスト
/// </summary>
public class WindowHandleStateManagerTests
{
    [Fact]
    public void SetState_And_TryGetState_WorksCorrectly()
    {
        // Arrange
        var manager = new WindowHandleStateManager();
        var hwnd = new IntPtr(0x12345);

        // Act
        manager.SetState(hwnd, true);

        // Assert - ウィンドウが有効でない場合はfalseを返す
        // 実際のテストではモックが必要だが、基本動作の確認
        var result = manager.TryGetState(hwnd, out bool state);

        // ウィンドウが存在しない場合はfalse
        Assert.False(result);
    }

    [Fact]
    public void SetState_WithZeroHandle_DoesNothing()
    {
        // Arrange
        var manager = new WindowHandleStateManager();

        // Act
        manager.SetState(IntPtr.Zero, true);

        // Assert
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void TryGetState_WithZeroHandle_ReturnsFalse()
    {
        // Arrange
        var manager = new WindowHandleStateManager();

        // Act
        var result = manager.TryGetState(IntPtr.Zero, out bool state);

        // Assert
        Assert.False(result);
        Assert.False(state);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        // Arrange
        var manager = new WindowHandleStateManager();
        manager.SetState(new IntPtr(1), true);
        manager.SetState(new IntPtr(2), false);

        // Act
        manager.Clear();

        // Assert
        Assert.Equal(0, manager.Count);
    }
}
