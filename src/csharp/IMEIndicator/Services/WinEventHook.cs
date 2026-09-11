// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;

namespace IMEIndicator.Services;

/// <summary>
/// EVENT_SYSTEM_FOREGROUND（フォアグラウンドウィンドウ切替）の検出。
/// 移植元: src/cpp/services/WinEventHook.h / .cpp と等価動作。EVENT_OBJECT_FOCUS は監視しない
/// （アプリ内コントロール間遷移で大量発火しデバウンスを浪費するため、移植元で廃止済み。
/// src/cpp/services/WinEventHook.h の同コメント参照）。
///
/// SetWinEventHook の WINEVENTPROC コールバックは context（ユーザーデータ）を受け取れないため、
/// プロセス内シングルトンとして実装する
/// （契約: specs/014-port-to-csharp/contracts/win32-interop-contract.md §コールバックの寿命と割り当て禁止）。
/// </summary>
public sealed class WinEventHook
{
    /// <summary>プロセス内で共有するシングルトンインスタンス。</summary>
    public static WinEventHook Instance { get; } = new();

    // WinEventProc delegate は static readonly フィールドに保持し GC 回収を防ぐ（契約必須事項）。
    private static readonly WinEventProc CallbackDelegate = HandleWinEvent;

    private nint _hook;
    private Action? _onForegroundChanged;
    private SynchronizationContext? _uiContext;

    // SynchronizationContext.Post に渡す委譲先。Start() で一度だけ生成してキャッシュし、
    // コールバック本体（HandleWinEvent）では新規に delegate を作らない
    // （契約: コールバック本体は new / ボックス化 / LINQ / 文字列連結 / ログ出力を禁止）。
    private SendOrPostCallback? _postToUiCallback;

    private WinEventHook()
    {
    }

    /// <summary>フックが有効かどうか。</summary>
    public bool IsRunning => _hook != 0;

    /// <summary>
    /// フックを開始する。<paramref name="onForegroundChanged"/> はフック検出時に直接ではなく、
    /// <paramref name="uiContext"/> を経由して（<see cref="SynchronizationContext.Post"/> のみ、
    /// Send は使わない）非同期に呼び出される。<paramref name="uiContext"/> が null の場合は
    /// 通知が行われない（呼び出し元が UI スレッドで <see cref="SynchronizationContext.Current"/> を
    /// 捕捉して渡すことを想定する）。
    /// </summary>
    /// <param name="onForegroundChanged">フォアグラウンドウィンドウ変化時に呼ぶコールバック。</param>
    /// <param name="uiContext">通知を Post する先の SynchronizationContext。</param>
    /// <returns>フック登録に成功したか（既に開始済みなら true）。</returns>
    public bool Start(Action onForegroundChanged, SynchronizationContext? uiContext)
    {
        ArgumentNullException.ThrowIfNull(onForegroundChanged);

        if (_hook != 0)
        {
            return true;
        }

        _onForegroundChanged = onForegroundChanged;
        _uiContext = uiContext;
        _postToUiCallback = PostToUi;

        _hook = NativeMethods.SetWinEventHook(
            NativeConstants.EVENT_SYSTEM_FOREGROUND,
            NativeConstants.EVENT_SYSTEM_FOREGROUND,
            0,
            CallbackDelegate,
            0,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT);

        bool success = _hook != 0;
        Log.Hook.Information("WinEventHook.Start: {Success}", success);
        return success;
    }

    /// <summary>フックを解除する。</summary>
    public void Stop()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = 0;
            Log.Hook.Information("WinEventHook.Stop");
        }

        _onForegroundChanged = null;
        _uiContext = null;
        _postToUiCallback = null;
    }

    // SynchronizationContext.Post のターゲット（UI スレッド側で実行される）。state は使わない。
    private void PostToUi(object? state) => _onForegroundChanged?.Invoke();

    // WINEVENTPROC 本体。契約により new / ボックス化 / LINQ / 文字列連結 / ログ出力を行わない。
    // UI へは Post のみで委譲する（Send は禁止）。イベント種別は SetWinEventHook 登録時点で
    // EVENT_SYSTEM_FOREGROUND のみに絞られているため、引数の内容を判定する必要はない
    // （移植元 WinEventHook.cpp の winEventProc と同じ、引数を無視する実装）。
    private static void HandleWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        WinEventHook self = Instance;
        SendOrPostCallback? postCallback = self._postToUiCallback;
        if (postCallback != null)
        {
            self._uiContext?.Post(postCallback, null);
        }
    }
}
