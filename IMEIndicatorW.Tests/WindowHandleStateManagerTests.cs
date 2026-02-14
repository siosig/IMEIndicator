using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// WindowHandleStateManagerのユニットテスト
/// NativeMethodsがモック不可（static P/Invoke）のため、
/// TryGetStateの成功パスはテストできないが、エントリ管理のロジックは網羅的にテスト
/// </summary>
public class WindowHandleStateManagerTests
{
    [Fact]
    public void SetState_AddsEntry()
    {
        var manager = new WindowHandleStateManager();

        manager.SetState(new IntPtr(0x12345), true);

        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void SetState_WithZeroHandle_DoesNothing()
    {
        var manager = new WindowHandleStateManager();

        manager.SetState(IntPtr.Zero, true);

        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void SetState_SameHandle_UpdatesState()
    {
        var manager = new WindowHandleStateManager();
        var hwnd = new IntPtr(0x100);

        manager.SetState(hwnd, true);
        manager.SetState(hwnd, false);

        // 同じハンドルなのでエントリ数は1のまま
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void SetState_MultipleHandles_TracksAll()
    {
        var manager = new WindowHandleStateManager();

        manager.SetState(new IntPtr(1), true);
        manager.SetState(new IntPtr(2), false);
        manager.SetState(new IntPtr(3), true);

        Assert.Equal(3, manager.Count);
    }

    [Fact]
    public void TryGetState_WithZeroHandle_ReturnsFalse()
    {
        var manager = new WindowHandleStateManager();

        var result = manager.TryGetState(IntPtr.Zero, out bool state);

        Assert.False(result);
        Assert.False(state);
    }

    [Fact]
    public void TryGetState_FakeHwnd_ReturnsFalse()
    {
        // 実際のウィンドウハンドルではないため、NativeMethods.IsWindow()がfalseを返し、
        // エントリは削除されてfalseが返る
        var manager = new WindowHandleStateManager();
        var hwnd = new IntPtr(0x12345);
        manager.SetState(hwnd, true);

        var result = manager.TryGetState(hwnd, out bool state);

        Assert.False(result);
    }

    [Fact]
    public void TryGetState_NonExistentHandle_ReturnsFalse()
    {
        var manager = new WindowHandleStateManager();

        var result = manager.TryGetState(new IntPtr(0x99999), out bool state);

        Assert.False(result);
        Assert.False(state);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var manager = new WindowHandleStateManager();
        manager.SetState(new IntPtr(1), true);
        manager.SetState(new IntPtr(2), false);

        manager.Clear();

        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task ConcurrentAccess_DoesNotThrow()
    {
        var manager = new WindowHandleStateManager();

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            int handle = i + 1;
            tasks.Add(Task.Run(() =>
            {
                manager.SetState(new IntPtr(handle), handle % 2 == 0);
                manager.TryGetState(new IntPtr(handle), out _);
            }));
        }

        await Task.WhenAll(tasks);
    }
}
