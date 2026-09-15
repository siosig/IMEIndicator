// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;
using IMEIndicator.Models;
// System.Threading と System.Windows.Forms は両方とも暗黙 global using で有効になっており、
// どちらにも Timer 型があるため素の "Timer" は CS0104（あいまい参照）になる。
// MouseTracker.cs / ProcessPriorityMonitor.cs と同じ理由で明示エイリアスを使う。
using SystemTimer = System.Threading.Timer;

// Log.LevelSwitch.MinimumLevel（LoggingLevelSwitch.MinimumLevel の型）との比較にのみ使う
// （017-fix-notepad-ime-display research.md R-7: 診断ログが無効なときは引数のボックス化も発生させない）。
using Serilog.Events;

namespace IMEIndicator.Services;

/// <summary>
/// IME 状態を統合的に監視するサービス。<see cref="KeyboardHook"/> + <see cref="WinEventHook"/> +
/// <see cref="MouseTracker"/> を統合し、<see cref="ImeDetector"/> + <see cref="PixelImeDetector"/> で
/// 状態を判定する。30ms デバウンス、200ms 楽観的更新検証窓を備える。
/// 移植元: src/cpp/services/IMEMonitor.h / .cpp の class IMEMonitor と等価動作。
/// </summary>
/// <remarks>
/// <see cref="KeyboardHook"/> / <see cref="WinEventHook"/> はプロセス内シングルトンであり、本クラスが
/// それらへコールバックを登録・解除する形で利用する（移植元と同じ設計）。<see cref="MouseTracker"/> は
/// 本クラスが所有し、<see cref="Start"/> と同時に無条件で起動する（表示/非表示では起動・停止しない。
/// research.md R-4 参照。表示制御は位置更新の受け手側の責務）。
/// </remarks>
public sealed class ImeMonitor : IDisposable
{
    private const int DebounceDelayMs = 30;
    private const int VerificationDelayMs = 200;

    // IME 切替キーの仮想キー値は ImeStateRules に集約した（017-fix-notepad-ime-display research.md R-5）。
    // 単一の情報源にするため、ここには局所定数を複製しない。

    private readonly object _stateLock = new();
    private readonly MouseTracker _mouseTracker = new();

    private SystemTimer? _debounceTimer;
    private int _runningFlag; // 0 = 停止、1 = 稼働中（Interlocked で操作）
    private int _isChecking;  // 0 = 空き、1 = 実行中（checkIMEState の多重実行防止）
    private bool _disposed;

    // === _stateLock 配下でのみ読み書きする状態 ===
    private LanguageInfo _lastState = new(LanguageType.English, false);
    private nint _lastForegroundWindow;
    private bool _trackedImeState;
    private LanguageType? _trackedLanguageForTerminal;
    private long _optimisticUpdateTimestampMs;

    // 推定対象キー（ImeStateRules.InferFromKey が null を返さないキー）の押下ごとに 1 増やす。
    // CheckImeState は判定の開始時にこの値を控え、完了時に値が変わっていればその判定結果を古いとみなして
    // 捨てる（通知も追跡状態の再同期もしない）。判定の実行中に押されたキーの推定を、キー押下前の
    // 読み取り値で上書きしてしまう「表示 → 非表示 → 再表示」のちらつきを防ぐ
    // （017-fix-notepad-ime-display research.md R-4、FR-004、data-model.md §4）。
    private long _keyGeneration;

    // 移植元と同じくデッドコード（research.md R-5 参照）: App 側から設定されるが、
    // PixelImeDetector の動作には一切影響しない。保持のみ行い、FR-001/FR-002 に従い
    // 現行 C++ 版の（無効果な）振る舞いをそのまま再現する。
    private int _pixelVerificationIntervalMs = 2000;

    /// <summary>IME 状態が変化したときに発火する。移植元 IMEStateCallback。</summary>
    public event Action<LanguageInfo>? ImeStateChanged;

    /// <summary>マウスカーソル位置が変化したときに発火する。移植元 CursorPositionCallback。</summary>
    public event Action<int, int>? CursorPositionChanged;

    /// <summary>
    /// ピクセル検証の周期設定を保持する（内部コマンド 201、設定画面用）。<b>この値は
    /// <see cref="PixelImeDetector"/> の動作には影響しない</b>（移植元と同じデッドコード。
    /// research.md R-5 に詳細を記載）。
    /// </summary>
    public void SetPixelVerificationIntervalMs(int ms) => _pixelVerificationIntervalMs = ms;

    /// <summary>現在保持している最新の IME 状態（コールバック発火後の値）。</summary>
    public LanguageInfo CurrentState
    {
        get { lock (_stateLock) { return _lastState; } }
    }

    private bool IsRunning => Volatile.Read(ref _runningFlag) != 0;

    /// <summary>
    /// 監視開始。フック類とトラッカーを起動し、初回チェックを実行する。
    /// UI スレッド（<see cref="SynchronizationContext.Current"/> が WinForms のメッセージポンプに
    /// 紐づくスレッド）から呼ぶこと。<see cref="WinEventHook"/> の通知先としてそのコンテキストを使う。
    /// </summary>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.Exchange(ref _runningFlag, 1) != 0)
        {
            return true; // 既に稼働中
        }

        _debounceTimer = new SystemTimer(OnDebounceTick, null, Timeout.Infinite, Timeout.Infinite);

        KeyboardHook.Instance.SetImeKeyCallback(OnImeKeyPressed);
        KeyboardHook.Instance.SetLanguageSwitchCallback(OnLanguageSwitchDetected);
        bool kbdOk = KeyboardHook.Instance.Start();
        Log.Ime.Information("ImeMonitor.Start: KeyboardHook={KbdOk}", kbdOk);

        bool weOk = WinEventHook.Instance.Start(OnTriggerFired, SynchronizationContext.Current);
        Log.Ime.Information("ImeMonitor.Start: WinEventHook={WeOk}", weOk);

        _mouseTracker.PositionChanged += OnMousePositionChanged;
        _mouseTracker.Start();
        Log.Ime.Information("ImeMonitor.Start: MouseTracker started");

        // 初回チェック（移植元 start() 末尾の onTriggerFired() 呼び出しと同じ）。
        OnTriggerFired();
        return true;
    }

    /// <summary>監視停止。フック解除とリソース解放を行う。</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _runningFlag, 0) == 0)
        {
            return; // 既に停止済み
        }

        // タイマーを停止し、実行中のコールバックが完了するのを待つ
        // （移植元 WaitForThreadpoolTimerCallbacks(debounceTimer_, TRUE) と同じ意図）。
        if (_debounceTimer is not null)
        {
            using var waitHandle = new ManualResetEvent(false);
            if (_debounceTimer.Dispose(waitHandle))
            {
                waitHandle.WaitOne();
            }
            _debounceTimer = null;
        }

        // フックは singleton なのでコールバックだけ外す（移植元と同じ）。
        KeyboardHook.Instance.SetImeKeyCallback(null);
        KeyboardHook.Instance.SetLanguageSwitchCallback(null);
        KeyboardHook.Instance.Stop();

        WinEventHook.Instance.Stop();

        _mouseTracker.Stop();
        _mouseTracker.PositionChanged -= OnMousePositionChanged;

        Log.Ime.Information("ImeMonitor.Stop");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _mouseTracker.Dispose();
        _disposed = true;
    }

    // WinEventHook のコールバック先（フォアグラウンドウィンドウ変化）。移植元 onTriggerFired。
    private void OnTriggerFired()
    {
        if (!IsRunning || _debounceTimer is null)
        {
            return;
        }
        _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
    }

    // デバウンスタイマー満了時に呼ばれる（ThreadPool ワーカースレッド）。移植元 onDebounceFired。
    private void OnDebounceTick(object? state)
    {
        if (!IsRunning)
        {
            return;
        }
        CheckImeState(forceUpdate: false);
    }

    // 実際の IME チェック処理（多重実行防止つき）。移植元 checkIMEState。
    //
    // 017-fix-notepad-ime-display での変更点（research.md R-1〜R-4・R-6・R-7、contracts/
    // ime-state-rules-contract.md「ImeMonitor への組み込み」）: 判定規則を ImeStateRules に委譲し、
    // (1) IMM32 が読めた判定ではピクセル判定を呼ばない（IMM32 優先。旧実装はピクセル判定が
    // 常に IMM32 を上書きしていた）、(2) 確実な判定が得られたときだけ追跡状態を再同期する、
    // (3) 判定の実行中に押されたキーより古い結果は捨てる、(4) 判定不能時は追跡状態（キー推定）を
    // そのまま使い、表示を消さない（FR-012）、という 4 点を実現する。
    private void CheckImeState(bool forceUpdate)
    {
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
        {
            return;
        }

        try
        {
            nint hwndForeground = NativeMethods.GetForegroundWindow();
            if (hwndForeground == 0)
            {
                Log.Ime.Debug("ImeMonitor.CheckImeState: skipped (no foreground window)");
                return;
            }

            LanguageType? trackedLanguageForTerminal;
            long generationAtStart;
            lock (_stateLock)
            {
                trackedLanguageForTerminal = _trackedLanguageForTerminal;
                generationAtStart = _keyGeneration;
            }

            ImeDetectionResult detection = ImeDetector.GetCurrentImeStateEx(trackedLanguageForTerminal);
            LanguageType language = detection.State.Language;
            bool immIsOpen = detection.State.IsImeOn;
            bool immReliable = detection.ReliableStatus;

            // IMM32 が読めない日本語のときだけピクセル判定を呼ぶ（research.md R-2・R-9:
            // 判定 1 回あたりの UI Automation 検索を減らす。IMM32 が読めた判定はもう上書きされない）。
            bool? pixelIsOn = ImeStateRules.NeedsPixelFallback(language, immReliable)
                ? PixelImeDetector.Instance.DetectImeStateTimed(language)
                : null;

            bool windowChanged;
            lock (_stateLock)
            {
                windowChanged = hwndForeground != _lastForegroundWindow;
            }

            if (windowChanged)
            {
                lock (_stateLock)
                {
                    _lastForegroundWindow = hwndForeground;
                }
            }

            LanguageInfo state;
            bool stale;
            bool trackedSnapshot;
            ImeStateSource source;
            bool shouldFire;
            lock (_stateLock)
            {
                // research.md R-4: 判定開始時から世代が進んでいれば、この判定結果は古いとみなして捨てる。
                stale = _keyGeneration != generationAtStart;
                trackedSnapshot = _trackedImeState;

                ImeResolution resolution = ImeStateRules.Resolve(language, immIsOpen, immReliable, pixelIsOn, trackedSnapshot);
                source = resolution.Source;
                state = new LanguageInfo(language, resolution.IsImeOn);

                // research.md R-3: 確実な判定（IMM32/ピクセル）が得られたら、キー推定の追跡状態を合わせる。
                if (!stale && resolution.ResyncsTrackedState)
                {
                    _trackedImeState = resolution.IsImeOn;
                }

                bool stateChanged = state != _lastState;
                long elapsed = Environment.TickCount64 - _optimisticUpdateTimestampMs;
                shouldFire = ImeStateRules.ShouldFire(stale, stateChanged, forceUpdate, elapsed, VerificationDelayMs);

                if (shouldFire)
                {
                    _lastState = state;
                }
            }

            if (Log.LevelSwitch.MinimumLevel <= LogEventLevel.Debug)
            {
                Log.Ime.Debug(
                    "ImeMonitor.CheckImeState: fg=0x{ForegroundWindow:X}/{ForegroundThreadId} query=0x{QueryWindow:X}/{QueryThreadId} " +
                    "lang={Language} imm={ImmIsOpen} reliable={ImmReliable} pixel={PixelIsOn} tracked={TrackedImeState} source={Source} ime={ImeOn} stale={Stale} fire={Fire}",
                    detection.ForegroundWindow, detection.ForegroundThreadId, detection.QueryWindow, detection.QueryThreadId,
                    language, immIsOpen, immReliable, pixelIsOn, trackedSnapshot, source, state.IsImeOn, stale, shouldFire);
            }

            if (shouldFire)
            {
                ImeStateChanged?.Invoke(state);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    // KeyboardHook のコールバック先（IME 切替キー）。低レベルフックの呼び出し元スレッド
    // （＝フックを登録した UI スレッド）で直接実行されるため、追加のマーシャリングは不要
    // （移植元 onIMEKeyPressed も同じスレッドで直接実行される）。軽量な処理に留めること。
    //
    // 017-fix-notepad-ime-display での変更点: 推定規則を ImeStateRules.InferFromKey に委譲し
    // （VK_IME_ON / VK_IME_OFF を推定対象に追加。research.md R-5）、推定対象キーの押下ごとに
    // _keyGeneration を進める（判定中に押された古い結果を捨てるための世代番号。research.md R-4）。
    private void OnImeKeyPressed(int vkCode)
    {
        ImeKeyInference? inference;
        long generation = 0;
        LanguageInfo optimisticState = default;

        lock (_stateLock)
        {
            inference = ImeStateRules.InferFromKey(vkCode, _trackedImeState);
            if (inference is not null)
            {
                _trackedImeState = inference.Value.IsImeOn;
                if (inference.Value.ImpliesJapaneseLanguage)
                {
                    _trackedLanguageForTerminal = LanguageType.Japanese;
                }

                _keyGeneration++;
                generation = _keyGeneration;

                optimisticState = new LanguageInfo(LanguageType.Japanese, _trackedImeState);
                _lastState = optimisticState;
                _optimisticUpdateTimestampMs = Environment.TickCount64;
            }
        }

        if (inference is null)
        {
            // 推定対象外のキー（VK_KANA・VK_CONVERT・VK_NONCONVERT 等）。既存どおり vk のみ記録する。
            Log.Ime.Debug("ImeMonitor.OnImeKeyPressed: vk=0x{VkCode:X2}", vkCode);
            return;
        }

        if (Log.LevelSwitch.MinimumLevel <= LogEventLevel.Debug)
        {
            Log.Ime.Debug(
                "ImeMonitor.OnImeKeyPressed: vk=0x{VkCode:X2} inferred={InferredImeOn} tracked={TrackedImeState} generation={KeyGeneration}",
                vkCode, inference.Value.IsImeOn, optimisticState.IsImeOn, generation);
        }

        ImeStateChanged?.Invoke(optimisticState);

        // 200ms 後にピクセル判定で答え合わせ（移植元と同じ）。
        _debounceTimer?.Change(VerificationDelayMs, Timeout.Infinite);
    }

    // KeyboardHook のコールバック先（Win+Space 言語切替）。移植元 onLanguageSwitchDetected。
    private void OnLanguageSwitchDetected()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        nint hkl = NativeMethods.GetKeyboardLayout(threadId);
        int langId = (int)(hkl & 0xFFFF);

        lock (_stateLock)
        {
            _trackedLanguageForTerminal = ImeDetector.GetLanguageType(langId);
        }

        CheckImeState(forceUpdate: true);
    }

    private void OnMousePositionChanged(int x, int y) => CursorPositionChanged?.Invoke(x, y);
}
