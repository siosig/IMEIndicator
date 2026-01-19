using System.Runtime.InteropServices;

namespace IMEIndicatorClock.Services;

/// <summary>
/// IME検出の共通ヘルパーメソッド
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
    /// 言語IDから言語タイプを判定
    /// </summary>
    public static LanguageType GetLanguageType(int langId)
    {
        int primaryLang = langId & 0xFF;

        return langId switch
        {
            // 東アジア
            NativeMethods.LANG_JAPANESE => LanguageType.Japanese,
            NativeMethods.LANG_CHINESE_SIMPLIFIED => LanguageType.ChineseSimplified,
            NativeMethods.LANG_CHINESE_TRADITIONAL => LanguageType.ChineseTraditional,
            NativeMethods.LANG_CHINESE_TRADITIONAL_HK => LanguageType.ChineseTraditional,

            // 東南アジア
            NativeMethods.LANG_THAI => LanguageType.Thai,
            NativeMethods.LANG_VIETNAMESE => LanguageType.Vietnamese,
            NativeMethods.LANG_MYANMAR => LanguageType.Myanmar,
            NativeMethods.LANG_KHMER => LanguageType.Khmer,
            NativeMethods.LANG_LAO => LanguageType.Lao,

            // 南アジア
            NativeMethods.LANG_HINDI => LanguageType.Hindi,
            NativeMethods.LANG_BENGALI_IN => LanguageType.Bengali,
            NativeMethods.LANG_BENGALI_BD => LanguageType.Bengali,
            NativeMethods.LANG_TAMIL => LanguageType.Tamil,
            NativeMethods.LANG_TELUGU => LanguageType.Telugu,
            NativeMethods.LANG_NEPALI => LanguageType.Nepali,
            NativeMethods.LANG_SINHALA => LanguageType.Sinhala,

            // 中央アジア
            NativeMethods.LANG_MONGOLIAN => LanguageType.Mongolian,
            NativeMethods.LANG_MONGOLIAN_CN => LanguageType.Mongolian,

            // 中東
            NativeMethods.LANG_ARABIC => LanguageType.Arabic,
            NativeMethods.LANG_ARABIC_EGYPT => LanguageType.Arabic,
            NativeMethods.LANG_ARABIC_UAE => LanguageType.Arabic,
            NativeMethods.LANG_PERSIAN => LanguageType.Persian,
            NativeMethods.LANG_HEBREW => LanguageType.Hebrew,

            // ヨーロッパ（キリル・ギリシャ）
            NativeMethods.LANG_UKRAINIAN => LanguageType.Ukrainian,
            NativeMethods.LANG_RUSSIAN => LanguageType.Russian,
            NativeMethods.LANG_GREEK => LanguageType.Greek,

            // プライマリ言語コードで判定
            _ when primaryLang == NativeMethods.LANG_PRIMARY_KOREAN => LanguageType.Korean,
            _ when primaryLang == NativeMethods.LANG_PRIMARY_ARABIC => LanguageType.Arabic,
            _ when primaryLang == NativeMethods.LANG_PRIMARY_BENGALI => LanguageType.Bengali,
            _ when primaryLang == NativeMethods.LANG_PRIMARY_MONGOLIAN => LanguageType.Mongolian,
            _ when primaryLang == 0x09 => LanguageType.English,
            _ => LanguageType.Other
        };
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
                DbgLog.Log(6, $"WM_IME_CONTROL成功: IME={lpdwResult != IntPtr.Zero}");
                return (lpdwResult != IntPtr.Zero, true);
            }
        }

        // 方法2: ImmGetContext
        IntPtr hIMC = NativeMethods.ImmGetContext(hwndForeground);
        if (hIMC != IntPtr.Zero)
        {
            bool isOpen = NativeMethods.ImmGetOpenStatus(hIMC);
            NativeMethods.ImmReleaseContext(hwndForeground, hIMC);
            DbgLog.Log(6, $"ImmGetContext成功 (hwndForeground): IME={isOpen}");
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
                DbgLog.Log(6, $"ImmGetContext成功 (hwndFocus): IME={isOpen}");
                return (isOpen, true);
            }
        }

        // 方法3: 候補ウィンドウ検出
        bool? fallbackResult = DetectIMEByCandidateWindow(hwndForeground);
        if (fallbackResult.HasValue)
        {
            DbgLog.Log(5, $"候補ウィンドウ検出: IME={fallbackResult.Value}");
            return (fallbackResult.Value, true);
        }

        return (false, false);
    }

    /// <summary>
    /// 候補ウィンドウの存在でIME状態を検出
    /// </summary>
    private static readonly HashSet<string> CandidateWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "UIWndClass",
        "CandidateWindow",
        "Microsoft.IME.UIManager.CandidateWindow",
        "IME_Candidate",
        "MSCTFIME UI",
        "IME",
        "IMECLASSUI"
    };

    private static bool? DetectIMEByCandidateWindow(IntPtr ownerHwnd)
    {
        if (ownerHwnd == IntPtr.Zero) return null;

        var ownedWindows = new List<IntPtr>();

        NativeMethods.EnumWindows(new NativeMethods.EnumWindowsProc((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            IntPtr owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
            if (owner == ownerHwnd)
            {
                if (NativeMethods.GetWindowRect(hwnd, out var rect))
                {
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    if (w >= 10 && h >= 10)
                    {
                        ownedWindows.Add(hwnd);
                    }
                }
            }
            return true;
        }), IntPtr.Zero);

        foreach (var hwnd in ownedWindows)
        {
            string className = NativeMethods.GetWindowClassName(hwnd);

            if (CandidateWindowClasses.Contains(className))
            {
                DbgLog.Log(6, $"候補ウィンドウ検出: class={className}");
                return true;
            }

            foreach (var hint in CandidateWindowClasses)
            {
                if (className.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    DbgLog.Log(6, $"候補ウィンドウ検出(部分一致): class={className}");
                    return true;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 現在のIME状態を取得（拡張版）
    /// </summary>
    public static (LanguageInfo state, bool reliableStatus) GetCurrentIMEStateEx(
        LanguageType? trackedLanguageForTerminal,
        out string debugInfo)
    {
        debugInfo = "";

        var hwndForeground = NativeMethods.GetForegroundWindow();
        if (hwndForeground == IntPtr.Zero)
        {
            debugInfo = "[IME] フォアグラウンドウィンドウなし";
            return (new LanguageInfo(LanguageType.English, false), false);
        }

        string windowTitle = NativeMethods.GetWindowTitle(hwndForeground);
        string processName = NativeMethods.GetProcessName(hwndForeground);
        string className = NativeMethods.GetWindowClassName(hwndForeground);

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwndForeground, out uint processId);

        var guiInfo = new NativeMethods.GUITHREADINFO();
        guiInfo.cbSize = Marshal.SizeOf(guiInfo);

        IntPtr hwndTarget = hwndForeground;
        string focusInfo = "same";
        uint focusThreadId = threadId;
        if (NativeMethods.GetGUIThreadInfo(threadId, ref guiInfo))
        {
            if (guiInfo.hwndFocus != IntPtr.Zero && guiInfo.hwndFocus != hwndForeground)
            {
                hwndTarget = guiInfo.hwndFocus;
                focusThreadId = NativeMethods.GetWindowThreadProcessId(hwndTarget, out _);
                focusInfo = $"0x{hwndTarget:X} ({NativeMethods.GetWindowClassName(hwndTarget)})";
            }
        }

        IntPtr hkl = NativeMethods.GetKeyboardLayout(focusThreadId);
        int langId = (int)hkl & 0xFFFF;

        var (imeOpen, imeSuccess) = GetIMEOpenStatusEx(hwndTarget, hwndForeground);

        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwndForeground);
        string imeWndInfo = imeWnd != IntPtr.Zero ? $"0x{imeWnd:X}" : "NG";

        IntPtr hIMC1 = NativeMethods.ImmGetContext(hwndTarget);
        IntPtr hIMC2 = NativeMethods.ImmGetContext(hwndForeground);
        if (hIMC1 != IntPtr.Zero) NativeMethods.ImmReleaseContext(hwndTarget, hIMC1);
        if (hIMC2 != IntPtr.Zero) NativeMethods.ImmReleaseContext(hwndForeground, hIMC2);

        bool reliableStatus = imeSuccess;

        string statusInfo = $"IMEWnd:{imeWndInfo}, Status:{(imeSuccess ? "OK" : "NG")}, " +
                           $"IMC(focus:{(hIMC1 != IntPtr.Zero ? "OK" : "NG")}, fg:{(hIMC2 != IntPtr.Zero ? "OK" : "NG")}), " +
                           $"Reliable:{(reliableStatus ? "OK" : "NG")}";

        bool isTerminalProcess = TerminalProcesses.Contains(processName);

        debugInfo = $"[IME] {processName} | \"{windowTitle}\" | class={className} | " +
                    $"hwnd=0x{hwndForeground:X} | focus={focusInfo} | " +
                    $"lang=0x{langId:X4} | {statusInfo}";

        var language = GetLanguageType(langId);

        if (isTerminalProcess && trackedLanguageForTerminal.HasValue)
        {
            language = trackedLanguageForTerminal.Value;
            debugInfo += $" [TerminalLang:{language}]";
        }

        if (language == LanguageType.English)
        {
            imeOpen = false;
        }

        return (new LanguageInfo(language, imeOpen), reliableStatus);
    }
}
