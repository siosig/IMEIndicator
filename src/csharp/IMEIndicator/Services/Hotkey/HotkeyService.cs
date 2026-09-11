// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models.Hotkey;
using IMEIndicator.Services.Hotkey.Commands;

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// HookEngine + HotkeyManager + CommandExecutor のオーケストレーション層。
/// 移植元: src/cpp/services/hotkey/HotkeyService.h / .cpp。
/// App から Start/Stop/Reload で操作し、メッセージループ経由で WM_HOTKEY_RAW_KBD/MOUSE を
/// 受け取って登録済みホットキーを実行する。
/// </summary>
/// <remarks>
/// 設計方針は C++ 版と同じ: <see cref="HookEngine"/> と <see cref="HotkeyManager"/> は本クラスが
/// 単独所有し、<see cref="CommandExecutor"/> は静的クラス（メインウィンドウハンドルは
/// <see cref="CommandExecutor.MainWindowHandle"/> 経由で渡す）。設定画面での保存後、App から
/// <see cref="Reload"/> を呼ぶことでホットキーの追加・削除・キー組合せ変更を即座に有効化する。
/// </remarks>
public sealed class HotkeyService
{
    private readonly HotkeyManager _manager = new();
    private readonly HookEngine _engine = new();
    private nint _mainHwnd;
    private HookMode _currentMode = HookMode.Auto;

    /// <summary>サービスが動作中かどうか。移植元 <c>isRunning()</c>。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>登録済みホットキーの管理オブジェクト（UI 連携用）。移植元 <c>manager()</c>。</summary>
    public HotkeyManager Manager => _manager;

    /// <summary>
    /// メインウィンドウハンドルを保存し、CommandExecutor へ伝播したうえで HookEngine を起動する。
    /// 移植元 <c>start(HWND, HotkeySettings, HookMode)</c>。
    /// </summary>
    /// <param name="mainHwnd">ホットキー検出通知の送信先ウィンドウ（App のメッセージ専用ウィンドウ）。</param>
    /// <param name="settings">初期ロード対象のホットキー設定。</param>
    /// <param name="mode">フックモード。既定 <see cref="HookMode.Auto"/>。</param>
    /// <returns>起動成功時 true。</returns>
    public bool Start(nint mainHwnd, HotkeySettings settings, HookMode mode = HookMode.Auto)
    {
        if (IsRunning)
        {
            Log.Hotkey.Warning("HotkeyService.Start called while already running");
            return true;
        }

        if (mainHwnd == 0)
        {
            Log.Hotkey.Error("HotkeyService.Start: mainHwnd is null");
            return false;
        }

        _mainHwnd = mainHwnd;
        _currentMode = mode;

        // CommandExecutor にメインウィンドウハンドルを渡す（WindowCommands.MinimizeToTray 等で利用）。
        CommandExecutor.MainWindowHandle = _mainHwnd;

        // HotkeyManager に永続化済みエントリをロード。
        _manager.LoadFromHotkeySettings(settings.Hotkeys);

        if (!_engine.Start(_mainHwnd, _currentMode))
        {
            Log.Hotkey.Error("HookEngine.Start failed");
            _mainHwnd = 0;
            return false;
        }

        IsRunning = true;
        Log.Hotkey.Information("HotkeyService started: hotkeys={Count}, mode={Mode}", _manager.Count, _currentMode);

        // autoStart=true のエントリを実行（FR-018）。
        ExecuteAutoStartEntries();
        return true;
    }

    /// <summary>フックスレッド停止・登録解除。移植元 <c>stop()</c>。</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _engine.Stop();
        _manager.Clear();
        CommandExecutor.MainWindowHandle = 0;
        IsRunning = false;
        _mainHwnd = 0;
        Log.Hotkey.Information("HotkeyService stopped");
    }

    /// <summary>
    /// 設定変更時に呼ぶ: HookEngine を一旦停止し、HotkeyManager を新しい設定で再ロードしてから
    /// HookEngine を再起動する。移植元 <c>reload(const HotkeySettings&amp;)</c>。
    /// </summary>
    public bool Reload(HotkeySettings settings)
    {
        if (!IsRunning)
        {
            Log.Hotkey.Warning("HotkeyService.Reload called while not running");
            return false;
        }

        nint savedHwnd = _mainHwnd;
        HookMode savedMode = _currentMode;

        _engine.Stop();
        _manager.LoadFromHotkeySettings(settings.Hotkeys);

        if (!_engine.Start(savedHwnd, savedMode))
        {
            Log.Hotkey.Error("HotkeyService.Reload: HookEngine restart failed");
            IsRunning = false;
            return false;
        }

        Log.Hotkey.Information("HotkeyService reloaded: hotkeys={Count}", _manager.Count);
        return true;
    }

    /// <summary>
    /// メインウィンドウの WndProc で <c>WM_HOTKEY_RAW_KBD</c>/<c>WM_HOTKEY_RAW_MOUSE</c> を
    /// 受け取った際に呼ぶ。処理した場合は true を返す。移植元 <c>handleRawHookMessage</c>。
    /// </summary>
    public bool HandleRawHookMessage(uint msg, nint wParam, nint lParam)
    {
        if (!IsRunning)
        {
            return false;
        }

        return InputRouter.HandleRawHookMessage(msg, wParam, lParam, OnHotkeyDetected);
    }

    // ホットキー検出時の内部ハンドラ。HotkeyManager から該当エントリを取得し ExecuteEntry へ
    // dispatch する。移植元 onHotkeyDetected。
    private void OnHotkeyDetected(uint vkey, uint scanCode, uint modifiers)
    {
        HotKeyEntry? entry = _manager.FindHotkeyByKey((int)vkey, (int)modifiers);
        if (entry is null)
        {
            Log.Hotkey.Debug("hotkey detected but no matching entry: vk={Vkey:X} mods={Modifiers:X} scan={ScanCode:X}", vkey, modifiers, scanCode);
            return;
        }

        Log.Hotkey.Debug("hotkey matched: vk={Vkey:X} mods={Modifiers:X}", vkey, modifiers);
        ExecuteEntry(entry);
    }

    /// <summary>
    /// 指定エントリを実行する（内部コマンドなら <see cref="CommandExecutor"/>、exe 起動なら
    /// <see cref="LaunchOrActivate"/>）。ホットキー検出時の <see cref="OnHotkeyDetected"/> と、
    /// トレイメニューからの直接実行（<c>TrayIcon</c> の trayMenu=true 項目クリック、T069 で配線）の
    /// 両方から使う共通経路。<see cref="HotKeyEntry.Disable"/> の場合は何もしない
    /// （移植元 onHotkeyDetected の disable 判定と同じ。トレイメニュー自体は
    /// <c>disable=false</c> のエントリのみを表示するため通常到達しないが、防御的に判定する）。
    /// </summary>
    public void ExecuteEntry(HotKeyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Disable)
        {
            Log.Hotkey.Debug("hotkey is disabled, skipping");
            return;
        }

        // 実行は info レベルでログ（移植元コメント: cmd 実行直後にアプリが強制終了しても
        // ログが残るよう、失敗しうる実行の前に記録する）。
        Log.Hotkey.Information(
            "executing hotkey entry: cmd={Cmd} exe={Exe}",
            entry.Cmd, entry.Exe.Length == 0 ? "(none)" : entry.Exe);

        if (entry.IsCommand())
        {
            ExecuteError? result = CommandExecutor.Execute(entry.Cmd, entry.Args);
            if (result is null)
            {
                Log.Hotkey.Information("command executed OK: cmd={Cmd}", entry.Cmd);
            }
            else
            {
                Log.Hotkey.Warning("CommandExecutor.Execute failed: cmd={Cmd} err={Error}", entry.Cmd, result);
            }
        }
        else if (entry.Exe.Length > 0)
        {
            LaunchOrActivate(entry);
        }
    }

    // exe / URL / フォルダ起動。multInst=false の場合は起動済みウィンドウを前面化し、
    // 無ければ launchApp を呼ぶ。multInst=true の場合は常に launchApp（複数インスタンス許可）。
    private static void LaunchOrActivate(HotKeyEntry entry)
    {
        if (!entry.MultInst)
        {
            nint existing = HwndUtil.FindWindowByExeName(entry.Exe);
            if (existing != 0)
            {
                if (HwndUtil.BringWindowToFront(existing))
                {
                    Log.Hotkey.Debug("brought existing window to front for exe={Exe}", entry.Exe);
                    return;
                }

                // 前面化に失敗した場合は新規起動にフォールバック。
                Log.Hotkey.Debug("BringWindowToFront failed, falling back to launch");
            }
        }

        ExecuteError? result = ProcessCommands.LaunchApp(entry.Exe, entry.Args, entry.Dir, entry.Admin, entry.CmdShow);
        if (result is not null)
        {
            Log.Hotkey.Warning("LaunchApp failed: exe={Exe} err={Error}", entry.Exe, result);
        }
    }

    // autoStart=true のエントリを起動順に実行（FR-018）。移植元 executeAutoStartEntries。
    private void ExecuteAutoStartEntries()
    {
        int executed = 0;
        foreach (HotKeyEntry hk in _manager.Hotkeys)
        {
            if (!hk.AutoStart || hk.Disable)
            {
                continue;
            }

            if (hk.IsCommand())
            {
                ExecuteError? result = CommandExecutor.Execute(hk.Cmd, hk.Args);
                if (result is not null)
                {
                    Log.Hotkey.Warning("autoStart command failed: cmd={Cmd} err={Error}", hk.Cmd, result);
                }
            }
            else if (hk.Exe.Length > 0)
            {
                // autoStart の exe 起動は multInst 制御を適用しない（起動時の自動実行は常に新規プロセス。
                // 移植元コメントと同じ）。
                ExecuteError? result = ProcessCommands.LaunchApp(hk.Exe, hk.Args, hk.Dir, hk.Admin, hk.CmdShow);
                if (result is not null)
                {
                    Log.Hotkey.Warning("autoStart launch failed: exe={Exe} err={Error}", hk.Exe, result);
                }
            }

            executed++;
        }

        if (executed > 0)
        {
            Log.Hotkey.Information("executed {Count} autoStart entries", executed);
        }
    }
}
