namespace IMEIndicator.Services;

/// <summary>
/// IMEMonitor - キーボードイベント処理（日本語IME特化）
/// </summary>
public partial class IMEMonitor
{
    private void OnIMEKeyPressed(int vkCode)
    {
        const int VK_KANJI = 0x19;
        const int VK_OEM_AUTO = 0xF3;
        const int VK_OEM_ENLW = 0xF4;

        bool stateChanged = false;

        if (vkCode == VK_KANJI)
        {
            _trackedIMEState = !_trackedIMEState;
            stateChanged = true;
        }
        else if (vkCode == VK_OEM_AUTO)
        {
            _trackedIMEState = false;
            _trackedLanguageForTerminal = LanguageType.Japanese;
            stateChanged = true;
        }
        else if (vkCode == VK_OEM_ENLW)
        {
            _trackedIMEState = true;
            _trackedLanguageForTerminal = LanguageType.Japanese;
            stateChanged = true;
        }

        if (stateChanged)
        {
            // デバウンス処理: 最後のキー押下から一定時間後にピクセル判定
            OnTriggerFired();
        }
    }

    private void OnLanguageSwitchDetected()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        string processName = NativeMethods.GetProcessName(hwnd);
        bool isTerminal = IMEDetector_Common.TerminalProcesses.Contains(processName);

        if (isTerminal)
        {
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
                CheckIMEState2(forceUpdate: true);
            });
        }
    }
}
