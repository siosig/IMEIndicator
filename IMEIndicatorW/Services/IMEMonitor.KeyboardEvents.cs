using System.Runtime.InteropServices;

namespace IMEIndicatorClock.Services;

/// <summary>
/// IMEMonitor - キーボードイベント処理
/// </summary>
public partial class IMEMonitor
{
    private void OnIMEKeyPressed(int vkCode)
    {
        const int VK_HANGUL = 0x15;
        const int VK_KANJI = 0x19;
        const int VK_OEM_AUTO = 0xF3;
        const int VK_OEM_ENLW = 0xF4;

        bool stateChanged = false;

        if (vkCode == VK_KANJI)
        {
            _trackedIMEState = !_trackedIMEState;
            stateChanged = true;
        }
        else if (vkCode == VK_HANGUL)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            string processName = NativeMethods.GetProcessName(hwnd);
            bool isTerminal = IMEDetector_Common.TerminalProcesses.Contains(processName);

            uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

            var guiInfo = new NativeMethods.GUITHREADINFO();
            guiInfo.cbSize = Marshal.SizeOf(guiInfo);
            if (NativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) &&
                guiInfo.hwndFocus != IntPtr.Zero && guiInfo.hwndFocus != hwnd)
            {
                threadId = NativeMethods.GetWindowThreadProcessId(guiInfo.hwndFocus, out _);
            }

            IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
            int langId = (int)hkl & 0xFFFF;
            int primaryLang = langId & 0xFF;

            bool isKoreanByAPI = primaryLang == NativeMethods.LANG_PRIMARY_KOREAN;
            bool isKoreanByTracking = isTerminal && _trackedLanguageForTerminal == LanguageType.Korean;
            bool isPendingDetection = isTerminal && _languagePendingDetection;

            DbgLog.Log(4, $"VK_HANGUL: isKorean(API:{isKoreanByAPI}, Track:{isKoreanByTracking}, Pending:{isPendingDetection})");

            if (isKoreanByAPI || isKoreanByTracking || isPendingDetection)
            {
                _trackedLanguageForTerminal = LanguageType.Korean;
                _languagePendingDetection = false;
                _trackedIMEState = !_trackedIMEState;
                stateChanged = true;
                DbgLog.Log(4, $"韓国語確定 & IMEトグル: {_trackedIMEState}");
            }
            else
            {
                DbgLog.Log(5, $"VK_HANGUL検出（非韓国語）- 無視");
                return;
            }
        }
        else if (vkCode == VK_OEM_AUTO)
        {
            _trackedIMEState = false;
            _trackedLanguageForTerminal = LanguageType.Japanese;
            _languagePendingDetection = false;
            _useTrackedStateForJapanese = true;
            stateChanged = true;
            DbgLog.Log(5, "OEM_AUTO → 日本語確定");
        }
        else if (vkCode == VK_OEM_ENLW)
        {
            _trackedIMEState = true;
            _trackedLanguageForTerminal = LanguageType.Japanese;
            _languagePendingDetection = false;
            _useTrackedStateForJapanese = true;
            stateChanged = true;
            DbgLog.Log(5, "OEM_ENLW → 日本語確定");
        }

        if (stateChanged)
        {
            DbgLog.Log(4, $"IME状態変更 (KeyHook): {_trackedIMEState}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                CheckIMEState(forceUpdate: true);
            });
        }
    }

    private void OnLanguageSwitchDetected()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        string processName = NativeMethods.GetProcessName(hwnd);
        bool isTerminal = IMEDetector_Common.TerminalProcesses.Contains(processName);

        if (isTerminal)
        {
            _languagePendingDetection = true;
            DbgLog.Log(4, "Win+Space検出 - 次のIMEキーで言語判定待ち");
        }
        else
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(150);
                uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
                IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
                int langId = (int)hkl & 0xFFFF;
                var newLang = IMEDetector_Common.GetLanguageType(langId);
                _trackedLanguageForTerminal = newLang;

                if (newLang == LanguageType.Korean)
                {
                    _trackedIMEState = false;
                    DbgLog.Log(4, $"言語取得: lang=0x{langId:X4} -> Korean (영문で初期化)");
                }
                else
                {
                    DbgLog.Log(4, $"言語取得: lang=0x{langId:X4} -> {newLang}");
                }
                CheckIMEState(forceUpdate: true);
            });
        }
    }

    private void OnChineseIMEToggleDetected(string eventType)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

        var guiInfo = new NativeMethods.GUITHREADINFO();
        guiInfo.cbSize = Marshal.SizeOf(guiInfo);
        if (NativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) &&
            guiInfo.hwndFocus != IntPtr.Zero && guiInfo.hwndFocus != hwnd)
        {
            threadId = NativeMethods.GetWindowThreadProcessId(guiInfo.hwndFocus, out _);
        }

        IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
        int langId = (int)hkl & 0xFFFF;
        var language = IMEDetector_Common.GetLanguageType(langId);

        bool isChinese = language == LanguageType.ChineseTraditional ||
                         language == LanguageType.ChineseSimplified;

        if (isChinese)
        {
            if (eventType == "Shift")
            {
                _trackedIMEState = !_trackedIMEState;
                _useTrackedStateForChinese = true;
                DbgLog.Log(4, $"中国語IME 中/英トグル (Shift): {_trackedIMEState}");
            }
            else if (eventType == "CtrlSpace")
            {
                _trackedIMEState = !_trackedIMEState;
                _useTrackedStateForChinese = true;
                DbgLog.Log(4, $"中国語IME ON/OFFトグル (Ctrl+Space): {_trackedIMEState}");
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                CheckIMEState(forceUpdate: true);
            });
        }
        else
        {
            DbgLog.Log(5, $"中国語IMEトグル検出（非中国語: {language}）- 無視");
        }
    }
}
