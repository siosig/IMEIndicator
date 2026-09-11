// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime;
using IMEIndicator.Interop;
using IMEIndicator.Models;
using IMEIndicator.Models.Hotkey;
using IMEIndicator.Services;
using IMEIndicator.Services.Hotkey;
using IMEIndicator.Services.Hotkey.Commands;
using IMEIndicator.Settings;
using IMEIndicator.Views;

namespace IMEIndicator.App;

/// <summary>
/// アプリ全体のライフサイクル統括。<see cref="ImeMonitor"/> / <see cref="CursorIndicatorWindow"/> /
/// <see cref="BackgroundImageWindow"/> / <see cref="TrayIcon"/> / <see cref="PowerModeBackup"/> と
/// <c>/powertoggle</c> IPC（<see cref="PowerToggleIpc"/>）を統合する。移植元: src/cpp/app/App.h / .cpp。
/// </summary>
/// <remarks>
/// 表示判定は <see cref="ApplyWindowVisibility"/> の単一経路に集約する（移植元 applyWindowVisibility /
/// applyBackgroundImageVisibility と同じ設計）。ホットキー機能（<c>HotkeyService</c>、US4/T053〜T069）は
/// このタスク（T041、US1）の対象外のため、内部コマンド 200 番台のハンドラは <c>ImeIndicatorCommands</c>
/// （T064）からこのクラスの公開メソッドへ委譲される想定で用意してある。
/// </remarks>
public sealed class App : ApplicationContext
{
    private readonly SettingsManager _settingsManager = new();
    private readonly CursorIndicatorWindow _indicatorWindow = new();
    private readonly BackgroundImageWindow _backgroundImageWindow = new();
    private readonly TrayIcon _trayIcon = new();
    private readonly ImeMonitor _imeMonitor = new();
    private readonly ProcessPriorityService _priorityService = new();
    private readonly ProcessPriorityMonitor _priorityMonitor;
    private readonly PowerModeBackup _powerModeBackup = new();
    private readonly MessageOnlyWindow _messageWindow = new();
    private readonly HotkeyService _hotkeyService = new();
    private PowerToggleIpc.Listener? _powerToggleListener;
    private SettingsForm? _settingsForm;
    private bool _disposed;

    public App()
    {
        _priorityMonitor = new ProcessPriorityMonitor(_priorityService);
    }

    /// <summary>
    /// 内部コマンド（CommandExecutor、T066）がウィンドウ操作系コマンドの対象として使う、
    /// このプロセスのメッセージ専用ウィンドウハンドル。移植元 <c>App::messageHwnd()</c> に対応。
    /// </summary>
    public nint MainWindowHandle => _messageWindow.Handle;

    /// <summary>設定ファイル・ログ・電源モード復元・各サービスを初期化して起動する。</summary>
    public void Initialize()
    {
        // 常駐中は GC 一時停止を短く保つ（フックコールバックの遅延を避ける。research.md R-6）。
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        _settingsManager.Load();
        Log.Initialize(_settingsManager.Settings.LogLevel);
        Log.App.Information("App.Initialize: settingsDirectory={Dir}", _settingsManager.SettingsDirectory);

        // PowerModeBackup の前回異常終了復元（FR-011）。
        if (_powerModeBackup.TryLoad(out PowerMode previousMode))
        {
            PowerModeService.Set(previousMode);
            _powerModeBackup.Delete();
            Log.Power.Information("Restored power mode from backup: {Mode}", previousMode);
        }

        _messageWindow.EnsureCreated();
        _messageWindow.RawHookMessageHandler = _hotkeyService.HandleRawHookMessage;

        // ---- インジケーターウィンドウ ----
        _indicatorWindow.ApplySettings(_settingsManager.Settings.MouseCursorIndicator);
        _indicatorWindow.SetText(_settingsManager.Settings.ImeOnText);
        RefreshIndicatorColor();

        // ---- トレイアイコン ----
        _trayIcon.ToggleIndicatorRequested += () => ToggleIndicatorVisible();
        _trayIcon.ToggleBackgroundImageRequested += () => ToggleBackgroundImageVisible();
        _trayIcon.PowerModeSelected += mode =>
        {
            SetPowerMode(mode);
            // ui-parity-contract.md §1: メニュー経由でもバルーン通知する
            // （IME OFF 中はインジケーター自体が非表示で色変化が見えないため）。
            _trayIcon.ShowBalloon("IME Indicator", "電源モード: " + PowerModeService.GetDisplayName(mode));
        };
        _trayIcon.OpenSettingsRequested += OpenSettings;
        _trayIcon.ExitRequested += () => ExitThread();
        _trayIcon.HotkeyMenuProvider = GetTrayHotkeyMenuEntries;
        RefreshTrayChecks();
        _trayIcon.Show();

        // ---- 背景画像ウィンドウ ----
        // 初期化自体は失敗しない設計（Relayout 失敗時は警告ログのみ。LayeredWindow 参照）。

        // ---- ホットキー（HotkeyService、US4） ----
        // ImeIndicatorCommands（内部コマンド 200〜299）から本クラスの公開メソッドへ委譲するための
        // ハンドラを先に注入してから HotkeyService を起動する（移植元 App::initialize の
        // setIndicatorWindow 等サービス注入 + hotkeyService_->start の順序と同じ意図）。
        ImeIndicatorCommands.SetHandlers(new ImeIndicatorCommands.Handlers(
            ToggleIndicatorVisible: ToggleIndicatorVisible,
            TogglePixelDetection: TogglePixelDetection,
            ReloadSettings: ReloadSettings,
            ToggleBackgroundImageVisible: ToggleBackgroundImageVisible,
            TogglePowerModeAndNotify: TogglePowerModeAndNotify,
            ApplyHighPerformancePower: ApplyHighPerformancePower,
            RestorePowerBackup: RestorePowerBackup,
            SetRuleEnabled: SetRuleEnabled,
            PauseAllRules: PauseAllRules));
        if (!_hotkeyService.Start(MainWindowHandle, _settingsManager.Settings.HotkeySettings))
        {
            Log.App.Warning("HotkeyService failed to start, hotkeys will be unavailable");
        }

        // ---- IMEMonitor ----
        _imeMonitor.SetPixelVerificationIntervalMs(_settingsManager.Settings.PixelVerificationIntervalMs);
        _imeMonitor.ImeStateChanged += OnImeStateChanged;
        _imeMonitor.CursorPositionChanged += OnCursorPositionChanged;
        _imeMonitor.Start();

        // ---- ProcessPriorityMonitor ----
        if (_settingsManager.Settings.ProcessPriorityRules.Count > 0)
        {
            _priorityMonitor.Start(_settingsManager.Settings.ProcessPriorityRules, _settingsManager.Settings.PollingIntervalSeconds);
        }

        // ---- /powertoggle IPC 受信 ----
        _powerToggleListener = new PowerToggleIpc.Listener(SynchronizationContext.Current);
        _powerToggleListener.Signaled += TogglePowerModeAndNotify;

        // ---- 初回起動時に設定画面を自動表示 ----
        if (_settingsManager.Settings.IsFirstLaunch)
        {
            _settingsManager.Settings.IsFirstLaunch = false;
            _settingsManager.Save();
            OpenSettings();
        }
    }

    /// <summary>監視停止・バックアップ削除・設定保存を行い、メッセージループを終了する。</summary>
    public void Shutdown()
    {
        _powerToggleListener?.Dispose();
        _powerToggleListener = null;

        _hotkeyService.Stop();
        _priorityMonitor.Stop();
        _imeMonitor.Stop();

        // 正常終了の証としてバックアップを削除（次回起動で復元発火を防ぐ。移植元と同じ）。
        _powerModeBackup.Delete();

        // 未保存の変更が残っていれば書き出す（現行 C++ 版は各ミューテータで都度保存するため
        // 通常は no-op になるが、防御的に最終保存する）。
        _settingsManager.Save();

        _trayIcon.Dispose();
        _indicatorWindow.Dispose();
        _backgroundImageWindow.Dispose();
        PixelImeDetector.Instance.ReleaseResources();

        Log.App.Information("App.Shutdown");
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            Shutdown();
        }
        base.Dispose(disposing);
    }

    // ============================================================
    // 公開操作（トレイ・内部コマンド・設定画面の各入口から呼ばれる単一経路）
    // ============================================================

    /// <summary>カーソル追従インジケーターの表示/非表示を設定・保存し、即時反映する。</summary>
    public void SetIndicatorVisible(bool visible)
    {
        _settingsManager.Settings.MouseCursorIndicator.IsVisible = visible;
        _settingsManager.Save();

        if (visible)
        {
            ApplyWindowVisibility(_imeMonitor.CurrentState);
        }
        else
        {
            _indicatorWindow.Hide();
        }

        RefreshTrayChecks();
    }

    public void ToggleIndicatorVisible() => SetIndicatorVisible(!_settingsManager.Settings.MouseCursorIndicator.IsVisible);

    /// <summary>背景画像表示の有効/無効を設定・保存し、現在の IME 状態で表示反映する。</summary>
    public void SetBackgroundImageVisible(bool visible)
    {
        _settingsManager.Settings.BackgroundImage.IsVisible = visible;
        _settingsManager.Save();

        ApplyWindowVisibility(_imeMonitor.CurrentState);
        RefreshTrayChecks();
    }

    public void ToggleBackgroundImageVisible() => SetBackgroundImageVisible(!_settingsManager.Settings.BackgroundImage.IsVisible);

    /// <summary>電源モードを設定し、インジケーター色を更新する（通知は行わない）。</summary>
    public void SetPowerMode(PowerMode mode)
    {
        BackupCurrentPowerMode();
        PowerModeService.Set(mode);
        RefreshIndicatorColor();
        RefreshTrayChecks();
    }

    /// <summary><c>/powertoggle</c> 受信時・内部コマンド 210 用。電源モードをトグルしバルーン通知する。</summary>
    public void TogglePowerModeAndNotify()
    {
        BackupCurrentPowerMode();
        PowerMode next = PowerModeService.Toggle();
        RefreshIndicatorColor();
        RefreshTrayChecks();
        _trayIcon.ShowBalloon("IME Indicator", "電源モード: " + PowerModeService.GetDisplayName(next));
    }

    /// <summary>内部コマンド 211: 最適なパフォーマンスへ切り替える。</summary>
    public void ApplyHighPerformancePower() => SetPowerMode(PowerMode.BestPerformance);

    /// <summary>内部コマンド 212: 電源モードバックアップから復元する。</summary>
    public void RestorePowerBackup()
    {
        if (_powerModeBackup.TryLoad(out PowerMode mode))
        {
            PowerModeService.Set(mode);
            _powerModeBackup.Delete();
            RefreshIndicatorColor();
            RefreshTrayChecks();
        }
    }

    /// <summary>
    /// 内部コマンド 201: ピクセル検出有効/無効。移植元と同じく <c>pixelVerificationIntervalMs</c> を
    /// 0⇔2000 でトグルして保存するのみで、実際のピクセル検出動作には影響しない
    /// （research.md R-5 に詳細を記載したデッドコードをそのまま再現）。
    /// </summary>
    public void TogglePixelDetection()
    {
        AppSettings s = _settingsManager.Settings;
        s.PixelVerificationIntervalMs = s.PixelVerificationIntervalMs == 0 ? 2000 : 0;
        _settingsManager.Save();
        _imeMonitor.SetPixelVerificationIntervalMs(s.PixelVerificationIntervalMs);
        Log.Ime.Information("TogglePixelDetection: intervalMs={IntervalMs}", s.PixelVerificationIntervalMs);
    }

    /// <summary>内部コマンド 202: 設定ファイルを再読込し、各サービスへ反映する。</summary>
    public void ReloadSettings()
    {
        _settingsManager.Load();
        ApplySettings(_settingsManager.Settings);
    }

    /// <summary>内部コマンド 220/221: 指定プロセス優先度ルールの一時停止/再開。</summary>
    public void SetRuleEnabled(string processName, bool enabled)
    {
        bool changed = false;
        foreach (ProcessPriorityRule rule in _settingsManager.Settings.ProcessPriorityRules)
        {
            if (string.Equals(rule.NormalizedProcessName(), processName, StringComparison.OrdinalIgnoreCase))
            {
                rule.IsEnabled = enabled;
                changed = true;
            }
        }
        if (!changed)
        {
            return;
        }
        _settingsManager.Save();
        _priorityMonitor.UpdateRules(_settingsManager.Settings.ProcessPriorityRules);
    }

    /// <summary>内部コマンド 222: 全プロセス優先度ルールを一時停止する。</summary>
    public void PauseAllRules()
    {
        foreach (ProcessPriorityRule rule in _settingsManager.Settings.ProcessPriorityRules)
        {
            rule.IsEnabled = false;
        }
        _settingsManager.Save();
        _priorityMonitor.UpdateRules(_settingsManager.Settings.ProcessPriorityRules);
    }

    /// <summary>
    /// 設定画面（<see cref="SettingsForm"/>）から適用されたときに呼ぶ。各サービスへの反映・
    /// 現在の IME 状態での表示再評価までを一括で行う（移植元 setAppliedCallback と同じ内容。
    /// 設定ファイルへの保存自体は <see cref="SettingsForm"/> 側の責務で、このメソッドが
    /// 呼ばれる時点で既に保存済みである）。
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _indicatorWindow.ApplySettings(settings.MouseCursorIndicator);
        _indicatorWindow.SetText(settings.ImeOnText);
        _imeMonitor.SetPixelVerificationIntervalMs(settings.PixelVerificationIntervalMs);
        Log.SetLevel(settings.LogLevel);
        _priorityMonitor.UpdateRules(settings.ProcessPriorityRules);
        _priorityMonitor.UpdatePollingInterval(settings.PollingIntervalSeconds);
        // ホットキー設定を反映（追加・削除・キー組合せ変更を即座に有効化する。移植元
        // App.cpp の appliedCallback_ 内 hotkeyService_->reload と同じ）。
        _hotkeyService.Reload(settings.HotkeySettings);
        ApplyWindowVisibility(_imeMonitor.CurrentState);
        RefreshTrayChecks();
    }

    /// <summary>
    /// 設定画面を開く。既に開いていれば前面化する（移植元 <c>App::initialize</c> 内
    /// <c>setOpenSettingsCallback</c> ラムダの <c>if (settingsDialog_ &amp;&amp; settingsDialog_->isOpen()) return;</c>
    /// と同じ単一インスタンス制御）。<see cref="SettingsForm"/> は <see cref="Form.ShowDialog()"/> で
    /// アプリケーションモーダルに表示するため、このメソッドは設定画面が閉じるまで戻らない。
    /// </summary>
    public void OpenSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settingsManager)
        {
            AppliedCallback = ApplySettings,
            AccessDeniedCountCallback = () => _priorityService.AccessDeniedCount,
            AccessibilityProbeCallback = ProbeRuleAccessibility,
        };
        try
        {
            _settingsForm.ShowDialog();
        }
        finally
        {
            _settingsForm.Dispose();
            _settingsForm = null;
        }
    }

    /// <summary>
    /// <see cref="SettingsForm.AccessibilityProbeCallback"/> の実体。各ルールが管理者権限不足で
    /// 制御不能かどうかを判定する。移植元 <c>App::initialize</c> 内 <c>setAccessibilityProbeCallback</c>
    /// ラムダと同じロジック(無効なルールは対象外・ブロック扱いにしない)。
    /// </summary>
    private List<bool> ProbeRuleAccessibility(IReadOnlyList<ProcessPriorityRule> rules)
    {
        var blocked = new List<bool>(rules.Count);
        foreach (ProcessPriorityRule rule in rules)
        {
            blocked.Add(rule.IsValid() && !_priorityService.IsAccessibleForControl(rule.NormalizedProcessName()));
        }
        return blocked;
    }

    // ============================================================
    // 表示判定（単一経路）
    // ============================================================

    /// <summary>
    /// 表示判定の単一経路。カーソル追従インジケーターと背景画像、双方の表示/非表示をここで決定する。
    /// 移植元 <c>App::applyWindowVisibility</c> + <c>applyBackgroundImageVisibility</c>。
    /// </summary>
    public void ApplyWindowVisibility(LanguageInfo info)
    {
        bool imeOn = info.Language == LanguageType.Japanese && info.IsImeOn;

        MouseCursorIndicatorSettings cursorCfg = _settingsManager.Settings.MouseCursorIndicator;
        Log.App.Debug("ApplyWindowVisibility: lang={Lang} ime={ImeOn} cfgVisible={CfgVisible}", info.Language, info.IsImeOn, cursorCfg.IsVisible);
        if (cursorCfg.IsVisible && imeOn)
        {
            _indicatorWindow.Show();
        }
        else
        {
            _indicatorWindow.Hide();
        }

        // 背景画像は「インジケーター表示」と完全に独立（FR-009）。cursorCfg.IsVisible の分岐に
        // 巻き込まれないよう、ここで必ず評価する（移植元と同じ）。
        bool shouldShowBackground = _settingsManager.Settings.BackgroundImage.IsVisible && imeOn;
        if (shouldShowBackground)
        {
            _backgroundImageWindow.Show();
        }
        else
        {
            _backgroundImageWindow.Hide();
        }
    }

    // ============================================================
    // 内部イベントハンドラ
    // ============================================================

    // ImeMonitor.ImeStateChanged は ThreadPool スレッド（デバウンスタイマー経由）または
    // フックスレッド（≒UI スレッド）のいずれからも発火し得るため、常に UI スレッドへ Post して
    // 一貫させる（移植元の PostMessageW によるマーシャリングと同じ意図）。
    private void OnImeStateChanged(LanguageInfo info)
    {
        Log.App.Debug("OnImeStateChanged: lang={Lang} ime={ImeOn}", info.Language, info.IsImeOn);
        SynchronizationContext? ctx = SynchronizationContext.Current;
        if (ctx is not null)
        {
            ctx.Post(state => ApplyWindowVisibility((LanguageInfo)state!), info);
        }
        else
        {
            ApplyWindowVisibility(info);
        }
    }

    // MouseTracker は Forms.Timer（UI スレッド）ベースのため、直接更新可能（移植元と同じ）。
    private void OnCursorPositionChanged(int x, int y) => _indicatorWindow.MoveTo(new Point(x, y));

    private void RefreshIndicatorColor()
    {
        PowerMode mode = PowerModeService.GetCurrent();
        _indicatorWindow.SetPowerMode(mode);
    }

    private void RefreshTrayChecks()
    {
        _trayIcon.RefreshChecks(
            _settingsManager.Settings.MouseCursorIndicator.IsVisible,
            _settingsManager.Settings.BackgroundImage.IsVisible,
            PowerModeService.GetCurrent());
    }

    // ホットキーサブメニューの動的構築。trayMenu=true かつ disable=false のエントリのみを対象とし、
    // 登録順を保つ（移植元 TrayIcon.cpp buildContextMenu の trayMenu フィルタと同じ）。
    // ラベルは DisplayName（note 空なら exe）、両方空なら "(無題)"（移植元 TrayIcon.cpp 244 行目の
    // フォールバックと同じ。HotKeyEntry.DisplayName 自体はこの既定値を持たないため、ここで補う）。
    private IReadOnlyList<(string Label, Action Run)> GetTrayHotkeyMenuEntries()
    {
        var entries = new List<(string Label, Action Run)>();
        foreach (HotKeyEntry hk in _hotkeyService.Manager.Hotkeys)
        {
            if (!hk.TrayMenu || hk.Disable)
            {
                continue;
            }

            string label = hk.DisplayName.Length == 0 ? "(無題)" : hk.DisplayName;
            entries.Add((label, () => _hotkeyService.ExecuteEntry(hk)));
        }

        return entries;
    }

    private void BackupCurrentPowerMode() => _powerModeBackup.Save(PowerModeService.GetCurrent());

    // メッセージ専用ウィンドウ（HWND_MESSAGE）。可視ウィンドウを持たない本アプリで、
    // 内部コマンド（CommandExecutor、T066）が「自アプリのウィンドウ」を必要とする場合の
    // ハンドル提供元、かつ HookEngine が WM_HOTKEY_RAW_KBD/WM_HOTKEY_RAW_MOUSE を投稿する先。
    // 移植元 App::createMessageWindow + App::messageWndProc に対応。
    private sealed class MessageOnlyWindow : NativeWindow
    {
        /// <summary>
        /// WM_HOTKEY_RAW_KBD/WM_HOTKEY_RAW_MOUSE を受け取った際に呼ぶハンドラ
        /// （<see cref="HotkeyService.HandleRawHookMessage"/> を渡す想定）。true を返すと
        /// 処理済みとみなし DefWindowProc を呼ばない（移植元 App::messageWndProc と同じ）。
        /// </summary>
        public Func<uint, nint, nint, bool>? RawHookMessageHandler { get; set; }

        public void EnsureCreated()
        {
            if (Handle != 0)
            {
                return;
            }

            var cp = new CreateParams
            {
                ClassName = null,
                Caption = null,
                Style = 0,
                ExStyle = 0,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Parent = NativeConstants.HWND_MESSAGE,
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HookEngine.WM_HOTKEY_RAW_KBD || m.Msg == HookEngine.WM_HOTKEY_RAW_MOUSE)
            {
                Func<uint, nint, nint, bool>? handler = RawHookMessageHandler;
                if (handler is not null && handler((uint)m.Msg, m.WParam, m.LParam))
                {
                    m.Result = 0;
                    return;
                }
            }

            base.WndProc(ref m);
        }
    }
}
