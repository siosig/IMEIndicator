using System.Runtime.InteropServices;

namespace IMEIndicator.Services;

/// <summary>
/// IME検出の共通ヘルパーメソッド（日本語IME特化）
/// </summary>
public static class IMEDetector_Common
{
    /// <summary>
    /// IME状態取得が信頼できないターミナル系プロセス
    /// </summary>
    public static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell",
        "pwsh",
        "cmd",
        "WindowsTerminal",
        "conhost",
        "wezterm-gui",
        "alacritty",
        "mintty"
    };

    /// <summary>
    /// 言語IDから言語タイプを判定（日本語IME特化）
    /// </summary>
    public static LanguageType GetLanguageType(int langId)
    {
        return langId == NativeMethods.LANG_JAPANESE
            ? LanguageType.Japanese
            : LanguageType.English;
    }

    /// <summary>
    /// IME ON/OFF状態を取得（拡張版）
    /// </summary>
    public static (bool isOpen, bool success) GetIMEOpenStatusEx(IntPtr hwndFocus, IntPtr hwndForeground)
    {
        // 方法1: DefaultIMEWndにWM_IME_CONTROL送信
        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwndForeground);
        if (imeWnd != IntPtr.Zero)
        {
            IntPtr result = NativeMethods.SendMessageTimeout(
                imeWnd,
                (uint)NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_GETOPENSTATUS,
                IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG,
                100,
                out IntPtr lpdwResult);

            if (result != IntPtr.Zero)
            {
                return (lpdwResult != IntPtr.Zero, true);
            }
        }

        // 方法2: ImmGetContext
        IntPtr hIMC = NativeMethods.ImmGetContext(hwndForeground);
        if (hIMC != IntPtr.Zero)
        {
            bool isOpen = NativeMethods.ImmGetOpenStatus(hIMC);
            NativeMethods.ImmReleaseContext(hwndForeground, hIMC);
            return (isOpen, true);
        }

        // フォーカスウィンドウでも試行
        if (hwndFocus != IntPtr.Zero && hwndFocus != hwndForeground)
        {
            hIMC = NativeMethods.ImmGetContext(hwndFocus);
            if (hIMC != IntPtr.Zero)
            {
                bool isOpen = NativeMethods.ImmGetOpenStatus(hIMC);
                NativeMethods.ImmReleaseContext(hwndFocus, hIMC);
                return (isOpen, true);
            }
        }

        return (false, false);
    }

    /// <summary>
    /// 現在のIME状態を取得（日本語IME特化版）
    /// </summary>
    public static (LanguageInfo state, bool reliableStatus) GetCurrentIMEStateEx(
        LanguageType? trackedLanguageForTerminal)
    {
        var hwndForeground = NativeMethods.GetForegroundWindow();
        if (hwndForeground == IntPtr.Zero)
        {
            return (new LanguageInfo(LanguageType.English, false), false);
        }

#if DEBUG
        string windowTitle = NativeMethods.GetWindowTitle(hwndForeground);
        string className = NativeMethods.GetWindowClassName(hwndForeground);
#endif

        string processName = NativeMethods.GetProcessName(hwndForeground);

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwndForeground, out uint processId);

        var guiInfo = new NativeMethods.GUITHREADINFO();
        guiInfo.cbSize = Marshal.SizeOf(guiInfo);

        IntPtr hwndTarget = hwndForeground;
        uint focusThreadId = threadId;
        if (NativeMethods.GetGUIThreadInfo(threadId, ref guiInfo))
        {
            if (guiInfo.hwndFocus != IntPtr.Zero && guiInfo.hwndFocus != hwndForeground)
            {
                hwndTarget = guiInfo.hwndFocus;
                focusThreadId = NativeMethods.GetWindowThreadProcessId(hwndTarget, out _);
#if DEBUG
                string focusInfo = $"0x{hwndTarget:X} ({NativeMethods.GetWindowClassName(hwndTarget)})";
#endif
            }
        }

        IntPtr hkl = NativeMethods.GetKeyboardLayout(focusThreadId);
        int langId = (int)hkl & 0xFFFF;

        var (imeOpen, imeSuccess) = GetIMEOpenStatusEx(hwndTarget, hwndForeground);

#if DEBUG
        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwndForeground);
        string imeWndInfo = imeWnd != IntPtr.Zero ? $"0x{imeWnd:X}" : "NG";

        string statusInfo = $"IMEWnd:{imeWndInfo}, Status:{(imeSuccess ? "OK" : "NG")}, " +
                           $"Reliable:{(imeSuccess ? "OK" : "NG")}";
#endif

        bool reliableStatus = imeSuccess;
        bool isTerminalProcess = TerminalProcesses.Contains(processName);

        var language = GetLanguageType(langId);

        if (isTerminalProcess && trackedLanguageForTerminal.HasValue)
        {
            language = trackedLanguageForTerminal.Value;
        }

        // 日本語以外はすべてIME OFF（英語）と同じ表示
        if (language != LanguageType.Japanese)
        {
            imeOpen = false;
        }

        return (new LanguageInfo(language, imeOpen), reliableStatus);
    }
}
