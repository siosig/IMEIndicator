using System;

namespace IMEIndicatorClock.Services;

/// <summary>
/// Windowsイベントフック管理（EVENT_SYSTEM_FOREGROUND のみ監視）
/// EVENT_OBJECT_FOCUS は廃止: ノイズが多く、アプリ内コントロール間のフォーカス移動で
/// 大量に発火してデバウンサーを無駄にリセットするため
/// </summary>
public class WinEventHookManager : IDisposable
{
    private WinEventHookHandle? _hookForeground;
    private NativeMethods.WinEventDelegate? _procDelegate; // GC対策で保持
    private bool _isStarted;

    public event Action? FocusChanged;

    public void Start()
    {
        if (_isStarted) return;
        _isStarted = true;

        _procDelegate = new NativeMethods.WinEventDelegate(WinEventProc);

        // EVENT_SYSTEM_FOREGROUND のみ（アクティブウィンドウ切り替え時）
        var handle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _procDelegate, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (handle != IntPtr.Zero)
            _hookForeground = new WinEventHookHandle(handle);
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        FocusChanged?.Invoke();
    }

    public void Stop()
    {
        if (!_isStarted) return;
        _isStarted = false;

        _hookForeground?.Dispose();
        _hookForeground = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
