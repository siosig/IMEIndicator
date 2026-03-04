using System.Runtime.InteropServices;

namespace IMEIndicatorClock.Services;

/// <summary>
/// 低レベルマウスフック（WH_MOUSE_LL）でカーソル位置をリアルタイム取得
/// </summary>
public class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public NativeMethods.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Note: デリゲートコールバックを使用するため DllImport を維持
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;  // GC回収防止のためフィールドに保持
    private bool _disposed;

    /// <summary>
    /// マウスが移動したときに発生（x, y はスクリーン物理座標）
    /// </summary>
    public event Action<int, int>? MouseMoved;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    /// <summary>
    /// フックを開始する（UIスレッドから呼び出すこと）
    /// </summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        try
        {
            // .NET Core/5+ では GetModuleHandle(null) を使用
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                DbgLog.E($"マウスフック設定失敗 (Error: {error})");
            }
            else
            {
                DbgLog.Log(4, $"マウスフック設定成功 (Handle: 0x{_hookId:X})");
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "マウスフック例外");
        }
    }

    /// <summary>
    /// フックを停止する
    /// </summary>
    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            DbgLog.Log(4, "マウスフック解除");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
        {
            try
            {
                var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseMoved?.Invoke(s.pt.X, s.pt.Y);
            }
            catch (Exception ex)
            {
                DbgLog.Ex(ex, "マウスフック コールバック例外");
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
