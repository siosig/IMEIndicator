using System.Windows.Threading;

namespace IMEIndicatorClock.Services;

/// <summary>
/// IME状態を監視するサービス
/// </summary>
public partial class IMEMonitor : IDisposable
{
    private DispatcherTimer? _timer;
    private KeyboardHook? _keyboardHook;
    private LanguageInfo _lastState = new(LanguageType.English, false);
    private IntPtr _lastForegroundWindow = IntPtr.Zero;
    private bool _trackedIMEState = false;
    private static LanguageType? _trackedLanguageForTerminal = null;
    private static bool _languagePendingDetection = false;
    private bool _useTrackedStateForJapanese = false;
    private bool _useTrackedStateForChinese = false;
    private WindowHandleStateManager _windowKoreanIMEStates = new();
    private bool _disposed;

    // ピクセル判定による状態検証
    private DateTime _lastPixelVerification = DateTime.MinValue;
    private static int _pixelVerificationIntervalMs = 2000;
    private bool _usePixelStateForKorean = false;
    private bool _usePixelStateForChinese = false;
    private bool _usePixelStateForJapanese = false;

    /// <summary>
    /// デバッグログを有効にするかどうか（レガシー互換）
    /// </summary>
    public static bool DebugMode
    {
        get => DebugLogService.DebugLevel != 0;
        set => DebugLogService.DebugLevel = value ? -5 : 0;
    }

    /// <summary>
    /// 定期ピクセル検証間隔を設定する（0で無効化）
    /// </summary>
    public static void SetPixelVerificationInterval(int intervalMs)
    {
        _pixelVerificationIntervalMs = intervalMs;
        DbgLog.I($"ピクセル検証間隔を変更: {(intervalMs == 0 ? "無効" : $"{intervalMs}ms")}");
    }

    /// <summary>
    /// IME状態が変更されたときに発生するイベント
    /// </summary>
    public event Action<LanguageInfo>? IMEStateChanged;

    /// <summary>
    /// 現在のマウスカーソル位置
    /// </summary>
    public event Action<int, int>? CursorPositionChanged;

    /// <summary>
    /// ポーリング間隔（ミリ秒）
    /// </summary>
    public int PollingInterval { get; set; } = 100;

    /// <summary>
    /// 現在のIME状態
    /// </summary>
    public LanguageInfo CurrentState => _lastState;

    /// <summary>
    /// 監視を開始する
    /// </summary>
    public void Start()
    {
        if (_timer != null) return;

        DbgLog.I("IMEMonitor 監視開始");

        _keyboardHook = new KeyboardHook();
        _keyboardHook.IMEKeyPressed += OnIMEKeyPressed;
        _keyboardHook.LanguageSwitchDetected += OnLanguageSwitchDetected;
        _keyboardHook.ChineseIMEToggleDetected += OnChineseIMEToggleDetected;
        _keyboardHook.Start();
        DbgLog.I("キーボードフック開始完了");

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollingInterval)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        CheckIMEState();
    }

    /// <summary>
    /// 監視を停止する
    /// </summary>
    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        if (_keyboardHook != null)
        {
            _keyboardHook.IMEKeyPressed -= OnIMEKeyPressed;
            _keyboardHook.LanguageSwitchDetected -= OnLanguageSwitchDetected;
            _keyboardHook.ChineseIMEToggleDetected -= OnChineseIMEToggleDetected;
            _keyboardHook.Dispose();
            _keyboardHook = null;
        }

        DbgLog.I("IMEMonitor 監視停止");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        CheckIMEState();
        CheckCursorPosition();
    }

    private void CheckCursorPosition()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            CursorPositionChanged?.Invoke(point.X, point.Y);
        }
    }

    private void CheckIMEState(bool forceUpdate = false)
    {
        try
        {
            var hwndForeground = NativeMethods.GetForegroundWindow();
            var (currentState, reliableStatus) = IMEDetector_Common.GetCurrentIMEStateEx(
                _trackedLanguageForTerminal, out string debugInfo);

            string processName = NativeMethods.GetProcessName(hwndForeground);
            bool isTerminalProcess = IMEDetector_Common.TerminalProcesses.Contains(processName);

            bool windowChanged = hwndForeground != _lastForegroundWindow;
            if (windowChanged)
            {
                _useTrackedStateForJapanese = false;
                _useTrackedStateForChinese = false;
            }

            bool isKoreanIME = currentState.Language == LanguageType.Korean;
            bool isChineseIME = currentState.Language == LanguageType.ChineseTraditional ||
                                currentState.Language == LanguageType.ChineseSimplified;
            bool isJapaneseIME = currentState.Language == LanguageType.Japanese;
            bool languageChanged = currentState.Language != _lastState.Language;

            // 韓国語IME処理
            if (isKoreanIME)
            {
                currentState = ProcessKoreanIME(hwndForeground, currentState, windowChanged, languageChanged, ref debugInfo);
            }
            // 中国語IME処理
            else if (isChineseIME)
            {
                currentState = IMEDetector_Chinese.ProcessChineseIME(
                    hwndForeground, currentState,
                    ref _trackedIMEState, ref _usePixelStateForChinese, ref _useTrackedStateForChinese,
                    windowChanged, languageChanged,
                    _pixelVerificationIntervalMs, ref _lastPixelVerification, ref debugInfo);
            }
            // 日本語IME処理
            else if (isJapaneseIME)
            {
                currentState = IMEDetector_Japanese.ProcessJapaneseIME(
                    hwndForeground, currentState,
                    ref _trackedIMEState, ref _usePixelStateForJapanese,
                    _useTrackedStateForJapanese,
                    windowChanged, languageChanged, reliableStatus,
                    _pixelVerificationIntervalMs, ref _lastPixelVerification, ref debugInfo);
            }
            // その他の言語
            else if ((windowChanged || languageChanged) && !isTerminalProcess)
            {
                if (reliableStatus)
                {
                    _trackedIMEState = currentState.IsIMEOn;
                    DbgLog.Log(5, $"その他言語初期化(API): {currentState.IsIMEOn}");
                }
                else if (languageChanged)
                {
                    _trackedIMEState = true;
                    DbgLog.Log(5, $"その他言語初期化(デフォルト): ON");
                }
                _trackedLanguageForTerminal = currentState.Language;
            }
            else if (windowChanged && isTerminalProcess && !_trackedLanguageForTerminal.HasValue)
            {
                _trackedLanguageForTerminal = currentState.Language;
                DbgLog.Log(5, $"ターミナル初期化: 言語={currentState.Language}");
            }

            // ターミナル系またはIME状態不明な場合
            bool useTracked = isTerminalProcess || !reliableStatus;
            if (!isKoreanIME && !isChineseIME && !isJapaneseIME && useTracked && currentState.Language != LanguageType.English)
            {
                currentState = new LanguageInfo(currentState.Language, _trackedIMEState);
                debugInfo += isTerminalProcess ? $" [Terminal:{_trackedIMEState}]" : $" [Tracked:{_trackedIMEState}]";
            }
            else if (!isKoreanIME && !isChineseIME && !isJapaneseIME && reliableStatus)
            {
                _trackedIMEState = currentState.IsIMEOn;
            }

            if (windowChanged)
            {
                DbgLog.Log(4, $"[Window変更] {debugInfo}");
                _lastForegroundWindow = hwndForeground;
            }

            if (currentState.Language != _lastState.Language ||
                currentState.IsIMEOn != _lastState.IsIMEOn ||
                forceUpdate)
            {
                if (!windowChanged)
                {
                    DbgLog.Log(5, debugInfo);
                }
                DbgLog.Log(4, $"  → 変更検出: {_lastState.Language}/{_lastState.IsIMEOn} → {currentState.Language}/{currentState.IsIMEOn}");
                _lastState = currentState;
                IMEStateChanged?.Invoke(currentState);
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "IME状態チェックエラー");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
