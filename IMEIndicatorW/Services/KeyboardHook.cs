using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IMEIndicatorClock.Services;

/// <summary>
/// 低レベルキーボードフックでIME切り替えキーを検出（日本語IME特化）
/// </summary>
public partial class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // IME関連キー
    private const int VK_KANA = 0x15;            // カナ/かな (IME切り替え)
    private const int VK_KANJI = 0x19;           // 半角/全角
    private const int VK_CONVERT = 0x1C;         // 変換
    private const int VK_NONCONVERT = 0x1D;      // 無変換
    private const int VK_IME_ON = 0x16;          // IME ON (標準)
    private const int VK_IME_OFF = 0x1A;         // IME OFF (標準)
    private const int VK_OEM_AUTO = 0xF3;        // IME OFF (一部キーボード)
    private const int VK_OEM_ENLW = 0xF4;        // IME ON (一部キーボード)

    // 言語切り替えキー
    private const int VK_SPACE = 0x20;           // Space
    private const int VK_LWIN = 0x5B;            // Left Windows key
    private const int VK_RWIN = 0x5C;            // Right Windows key

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Note: デリゲートコールバックを使用するため DllImport を維持
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _disposed;

    /// <summary>
    /// IME切り替えキーが押されたときに発生
    /// </summary>
    public event Action<int>? IMEKeyPressed;

    /// <summary>
    /// 言語切り替え (Win+Space等) が検出されたときに発生
    /// </summary>
    public event Action? LanguageSwitchDetected;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        try
        {
            // .NET Core/5+では GetModuleHandle(null) を使用
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)hookStruct.vkCode;
                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;

                // 詳細デバッグ: 全キー出力（レベル6以上で有効）
                if (isKeyDown)
                {
                }

                // KeyDown時の処理
                if (isKeyDown)
                {
                    // IME関連キーを検出
                    if (vkCode == VK_KANA || vkCode == VK_KANJI || vkCode == VK_CONVERT || vkCode == VK_NONCONVERT ||
                        vkCode == VK_IME_ON || vkCode == VK_IME_OFF ||
                        vkCode == VK_OEM_AUTO || vkCode == VK_OEM_ENLW)
                    {
                        IMEKeyPressed?.Invoke(vkCode);
                    }

                    // Win+Space 言語切り替え検出
                    if (vkCode == VK_SPACE)
                    {
                        bool winPressed = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
                                          (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                        if (winPressed)
                        {
                            LanguageSwitchDetected?.Invoke();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// VKコードからキー名を取得
    /// </summary>
    private static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            VK_KANA => "KANA",
            VK_KANJI => "KANJI (半角/全角)",
            VK_CONVERT => "CONVERT (変換)",
            VK_NONCONVERT => "NONCONVERT (無変換)",
            VK_IME_ON => "IME_ON",
            VK_IME_OFF => "IME_OFF",
            VK_OEM_AUTO => "OEM_AUTO",
            VK_OEM_ENLW => "OEM_ENLW",
            _ => $"0x{vkCode:X2}"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
