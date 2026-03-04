namespace IMEIndicatorClock.Services;

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
            DbgLog.Log(5, "OEM_AUTO → 日本語確定");
        }
        else if (vkCode == VK_OEM_ENLW)
        {
            _trackedIMEState = true;
            _trackedLanguageForTerminal = LanguageType.Japanese;
            stateChanged = true;
            DbgLog.Log(5, "OEM_ENLW → 日本語確定");
        }

        if (stateChanged)
        {
            DbgLog.Log(4, $"IME状態変更 (KeyHook): {_trackedIMEState}");
            // デバウンス処理: 最後のキー押下から100ms後にピクセル判定
            ScheduleDebouncedCheck();
        }
    }

    /// <summary>
    /// デバウンス付きでCheckIMEState2をスケジュール
    /// 連打時は最後のキー押下から100ms後に1回だけ実行
    /// </summary>
    private void ScheduleDebouncedCheck()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // タイマーを遅延初期化（初回のみ作成）
            if (_keyDebounceTimer == null)
            {
                _keyDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(KeyDebounceIntervalMs)
                };
                _keyDebounceTimer.Tick += (s, e) =>
                {
                    _keyDebounceTimer?.Stop();
                    DbgLog.Log(4, "デバウンスタイマー発火 → CheckIMEState2");
                    CheckIMEState2(forceUpdate: true);
                };
            }

            // 既存のタイマーをリセットして再開始
            _keyDebounceTimer.Stop();
            _keyDebounceTimer.Start();
            DbgLog.Log(5, $"デバウンスタイマー開始 ({KeyDebounceIntervalMs}ms)");
        });
    }

    private void OnLanguageSwitchDetected()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        string processName = NativeMethods.GetProcessName(hwnd);
        bool isTerminal = IMEDetector_Common.TerminalProcesses.Contains(processName);

        if (isTerminal)
        {
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

                DbgLog.Log(4, $"言語取得: lang=0x{langId:X4} -> {newLang}");
                CheckIMEState2(forceUpdate: true);
            });
        }
    }
}
