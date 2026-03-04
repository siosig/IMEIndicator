using System.Windows.Threading;

namespace IMEIndicatorClock.Services;

/// <summary>
/// IME状態を監視するサービス（日本語IME特化）
/// </summary>
public partial class IMEMonitor : IDisposable
{
    private DispatcherTimer? _timer;
    private KeyboardHook? _keyboardHook;
    private MouseHook? _mouseHook;
    private LanguageInfo _lastState = new(LanguageType.English, false);
    private IntPtr _lastForegroundWindow = IntPtr.Zero;
    private bool _trackedIMEState = false;
    private LanguageType? _trackedLanguageForTerminal = null;
    private bool _disposed;

    // キー押下デバウンス用
    private DispatcherTimer? _keyDebounceTimer;
    private const int KeyDebounceIntervalMs = 100;

    // ピクセル判定による状態検証
    private DateTime _lastPixelVerification = DateTime.MinValue;
    private int _pixelVerificationIntervalMs = 2000;

    // CheckIMEState2 の多重実行防止（タイマーとデバウンスが重なる場合）
    private volatile int _isChecking = 0;

    /// <summary>
    /// 定期ピクセル検証間隔を設定する（0で無効化）
    /// </summary>
    public void SetPixelVerificationInterval(int intervalMs)
    {
        _pixelVerificationIntervalMs = intervalMs;
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
    public int PollingInterval { get; set; } = 300;

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

        _keyboardHook = new KeyboardHook();
        _keyboardHook.IMEKeyPressed += OnIMEKeyPressed;
        _keyboardHook.LanguageSwitchDetected += OnLanguageSwitchDetected;
        _keyboardHook.Start();

        _mouseHook = new MouseHook();
        _mouseHook.MouseMoved += (x, y) => CursorPositionChanged?.Invoke(x, y);
        _mouseHook.Start();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollingInterval)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        CheckIMEState2();
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

        // デバウンスタイマーも停止
        _keyDebounceTimer?.Stop();
        _keyDebounceTimer = null;

        if (_keyboardHook != null)
        {
            _keyboardHook.IMEKeyPressed -= OnIMEKeyPressed;
            _keyboardHook.LanguageSwitchDetected -= OnLanguageSwitchDetected;
            _keyboardHook.Dispose();
            _keyboardHook = null;
        }

        if (_mouseHook != null)
        {
            _mouseHook.Dispose();
            _mouseHook = null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // TSF + PixelDetector はブロッキング処理のためバックグラウンドで実行
        // UIスレッドを解放し WH_MOUSE_LL コールバックの遅延を防ぐ
        _ = Task.Run(() => CheckIMEState2());
    }

    /// <summary>
    /// IME状態をチェック（日本語IME特化版）
    /// タイマー（バックグラウンド）とデバウンス（UIスレッド）から呼ばれる可能性があるため多重実行を防止
    /// </summary>
    private void CheckIMEState2(bool forceUpdate = false)
    {
        if (System.Threading.Interlocked.Exchange(ref _isChecking, 1) == 1) return;
        try
        {
            var hwndForeground = NativeMethods.GetForegroundWindow();
            if (hwndForeground == IntPtr.Zero) return;

            var (currentState, reliableStatus) = IMEDetector_Common.GetCurrentIMEStateEx(
                _trackedLanguageForTerminal);

            bool windowChanged = hwndForeground != _lastForegroundWindow;

            // 日本語IMEの場合、PixelIMEDetectorでピクセル判定
            if (currentState.Language == LanguageType.Japanese)
            {
                var result = PixelIMEDetector.Instance.DetectIMEState2(currentState.Language);
                if (result.IsOn.HasValue)
                {
                    currentState = new LanguageInfo(currentState.Language, result.IsOn.Value);
                    _trackedIMEState = result.IsOn.Value;
                }
            }

            if (windowChanged)
            {
                _lastForegroundWindow = hwndForeground;
            }

            if (currentState.Language != _lastState.Language ||
                currentState.IsIMEOn != _lastState.IsIMEOn ||
                forceUpdate)
            {
                if (!windowChanged)
                {
                }
                _lastState = currentState;
                IMEStateChanged?.Invoke(currentState);
            }
        }
        catch (Exception ex)
        {
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isChecking, 0);
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
