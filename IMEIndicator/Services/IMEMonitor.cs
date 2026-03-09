using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IMEIndicator.Services;

/// <summary>
/// IME状態を監視するサービス（日本語IME特化）
/// </summary>
public partial class IMEMonitor : IDisposable
{
    private KeyboardHook? _keyboardHook;
    private MouseTracker? _mouseTracker;
    private WinEventHookManager? _winEventHook;

    private LanguageInfo _lastState = new(LanguageType.English, false);
    private IntPtr _lastForegroundWindow = IntPtr.Zero;
    private bool _trackedIMEState = false;
    private LanguageType? _trackedLanguageForTerminal = null;
    private bool _disposed;

    // デバウンス: 単一Timer.Change()でアロケーションゼロ
    private System.Threading.Timer? _debounceTimer;
    private const int DebounceDelayMs = 150;

    // ピクセル判定による状態検証
    private DateTime _lastPixelVerification = DateTime.MinValue;
    private int _pixelVerificationIntervalMs = 2000;

    // CheckIMEState2 の多重実行防止
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
    /// 現在のIME状態
    /// </summary>
    public LanguageInfo CurrentState => _lastState;

    /// <summary>
    /// 監視を開始する
    /// </summary>
    public void Start()
    {
        // デバウンスタイマーを事前確保（以降は Change() のみでアロケーションなし）
        _debounceTimer = new System.Threading.Timer(
            _ => CheckIMEState2(), null, Timeout.Infinite, Timeout.Infinite);

        _keyboardHook = new KeyboardHook();
        _keyboardHook.IMEKeyPressed += OnIMEKeyPressed;
        _keyboardHook.LanguageSwitchDetected += OnLanguageSwitchDetected;
        _keyboardHook.Start();

        _mouseTracker = new MouseTracker();
        _mouseTracker.MouseMoved += (x, y) => CursorPositionChanged?.Invoke(x, y);
        _mouseTracker.Start();

        _winEventHook = new WinEventHookManager();
        _winEventHook.FocusChanged += OnTriggerFired;
        _winEventHook.Start();

        // 初回チェック
        OnTriggerFired();
    }

    /// <summary>
    /// 監視を停止する
    /// </summary>
    public void Stop()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (_keyboardHook != null)
        {
            _keyboardHook.IMEKeyPressed -= OnIMEKeyPressed;
            _keyboardHook.LanguageSwitchDetected -= OnLanguageSwitchDetected;
            _keyboardHook.Dispose();
            _keyboardHook = null;
        }

        if (_mouseTracker != null)
        {
            _mouseTracker.Dispose();
            _mouseTracker = null;
        }

        if (_winEventHook != null)
        {
            _winEventHook.FocusChanged -= OnTriggerFired;
            _winEventHook.Dispose();
            _winEventHook = null;
        }
    }

    public void OnTriggerFired()
    {
        // Timer.Change() でデバウンスをリセット（アロケーションゼロ）
        _debounceTimer?.Change(DebounceDelayMs, Timeout.Infinite);
    }

    /// <summary>
    /// IME状態をチェック（日本語IME特化版）
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
                _lastState = currentState;
                IMEStateChanged?.Invoke(currentState);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IMEMonitor] CheckIMEState2 failed: {ex.Message}");
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _debounceTimer?.Dispose(); }
        catch (Exception ex) { Trace.TraceError($"[IMEMonitor] debounceTimer Dispose failed: {ex.Message}"); }
        _debounceTimer = null;

        try
        {
            if (_keyboardHook != null)
            {
                _keyboardHook.IMEKeyPressed -= OnIMEKeyPressed;
                _keyboardHook.LanguageSwitchDetected -= OnLanguageSwitchDetected;
                _keyboardHook.Dispose();
            }
        }
        catch (Exception ex) { Trace.TraceError($"[IMEMonitor] KeyboardHook Dispose failed: {ex.Message}"); }
        _keyboardHook = null;

        try { _mouseTracker?.Dispose(); }
        catch (Exception ex) { Trace.TraceError($"[IMEMonitor] MouseTracker Dispose failed: {ex.Message}"); }
        _mouseTracker = null;

        try
        {
            if (_winEventHook != null)
            {
                _winEventHook.FocusChanged -= OnTriggerFired;
                _winEventHook.Dispose();
            }
        }
        catch (Exception ex) { Trace.TraceError($"[IMEMonitor] WinEventHook Dispose failed: {ex.Message}"); }
        _winEventHook = null;

        GC.SuppressFinalize(this);
    }
}
