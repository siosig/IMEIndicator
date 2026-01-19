using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IMEIndicatorClock.Services;

/// <summary>
/// 低レベルキーボードフックでIME切り替えキーを検出
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

    // 中国語IME切り替えキー
    private const int VK_SHIFT = 0x10;           // Shift (中国語IME 中/英切り替え)
    private const int VK_LSHIFT = 0xA0;          // Left Shift
    private const int VK_RSHIFT = 0xA1;          // Right Shift
    private const int VK_CONTROL = 0x11;         // Ctrl
    private const int VK_LCONTROL = 0xA2;        // Left Ctrl
    private const int VK_RCONTROL = 0xA3;        // Right Ctrl

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

    // 単独Shift検出用（中国語IME）
    private bool _shiftDownAlone = false;  // Shiftが単独で押されているか
    private uint _shiftDownTime = 0;       // Shift押下時刻

    /// <summary>
    /// IME切り替えキーが押されたときに発生
    /// </summary>
    public event Action<int>? IMEKeyPressed;

    /// <summary>
    /// 言語切り替え (Win+Space等) が検出されたときに発生
    /// </summary>
    public event Action? LanguageSwitchDetected;

    /// <summary>
    /// 中国語IME切り替え (Ctrl+Space, 単独Shift) が検出されたときに発生
    /// eventType: "CtrlSpace" = Ctrl+Space, "Shift" = 単独Shift
    /// </summary>
    public event Action<string>? ChineseIMEToggleDetected;

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
                DbgLog.E($"キーボードフック設定失敗 (Error: {error})");
            }
            else
            {
                DbgLog.Log(4, $"キーボードフック設定成功 (Handle: 0x{_hookId:X})");
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "キーボードフック例外");
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            DbgLog.Log(4, "キーボードフック解除");
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
                bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

                // 詳細デバッグ: 全キー出力（レベル6以上で有効）
                if (isKeyDown)
                {
                    DbgLog.Log(6, $"Key: vkCode=0x{vkCode:X2}, scanCode=0x{hookStruct.scanCode:X2}, flags=0x{hookStruct.flags:X2}");
                }

                // ========================================
                // 単独Shift検出（中国語IME 中/英切り替え）
                // ========================================
                if (vkCode == VK_SHIFT || vkCode == VK_LSHIFT || vkCode == VK_RSHIFT)
                {
                    if (isKeyDown)
                    {
                        // Shiftが押された - 単独フラグをセット
                        _shiftDownAlone = true;
                        _shiftDownTime = hookStruct.time;
                    }
                    else if (isKeyUp && _shiftDownAlone)
                    {
                        // Shiftが離された - 単独だった場合のみトグル
                        uint elapsed = hookStruct.time - _shiftDownTime;
                        // 500ms以内の短いShift押下のみを単独Shiftとして扱う
                        if (elapsed < 500)
                        {
                            DbgLog.Log(4, $"単独Shift検出 ({elapsed}ms) - 中国語IME 中/英切り替え");
                            ChineseIMEToggleDetected?.Invoke("Shift");
                        }
                        _shiftDownAlone = false;
                    }
                }
                // Shift以外のキーが押されたら単独Shiftフラグをリセット
                else if (isKeyDown && _shiftDownAlone)
                {
                    // モディファイアキー（Ctrl, Alt, Win）は除外
                    if (vkCode != VK_CONTROL && vkCode != VK_LCONTROL && vkCode != VK_RCONTROL &&
                        vkCode != VK_LWIN && vkCode != VK_RWIN &&
                        vkCode != 0x12 /* VK_MENU (Alt) */)
                    {
                        _shiftDownAlone = false;
                    }
                }

                // KeyDown時の処理
                if (isKeyDown)
                {
                    // IME関連キーを検出
                    if (vkCode == VK_KANA || vkCode == VK_KANJI || vkCode == VK_CONVERT || vkCode == VK_NONCONVERT ||
                        vkCode == VK_IME_ON || vkCode == VK_IME_OFF ||
                        vkCode == VK_OEM_AUTO || vkCode == VK_OEM_ENLW)
                    {
                        DbgLog.Log(5, $"IMEキー検出: vkCode=0x{vkCode:X2} ({GetKeyName(vkCode)})");
                        IMEKeyPressed?.Invoke(vkCode);
                    }

                    // Space キー
                    if (vkCode == VK_SPACE)
                    {
                        // Win+Space 言語切り替え検出
                        bool winPressed = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
                                          (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                        if (winPressed)
                        {
                            DbgLog.Log(4, "Win+Space検出 - 言語切り替え");
                            LanguageSwitchDetected?.Invoke();
                        }

                        // Ctrl+Space 中国語IME ON/OFF検出
                        bool ctrlPressed = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 ||
                                           (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
                        if (ctrlPressed && !winPressed)
                        {
                            DbgLog.Log(4, "Ctrl+Space検出 - 中国語IME ON/OFF");
                            ChineseIMEToggleDetected?.Invoke("CtrlSpace");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "キーボードフック コールバック例外");
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
