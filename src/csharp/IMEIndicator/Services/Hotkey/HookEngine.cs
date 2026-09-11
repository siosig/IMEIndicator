// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;
using IMEIndicator.Models.Hotkey;

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// WH_KEYBOARD_LL / WH_MOUSE_LL フックを専用スレッドで管理する。
/// 移植元: src/cpp/services/hotkey/HookEngine.h / .cpp のクラス <c>HookEngine</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>重要（実装時に実際のソースを読んで確認した、C++ 版の実際の挙動）</b>:
/// tasks.md の T067 説明は「<c>auto</c> は <c>RegisterHotKey</c> を優先し、表現できないエントリのみ
/// 低レベルフックを使う」としているが、これは事実と異なる。<c>src/cpp</c> 全体を検索しても
/// <c>RegisterHotKey</c> の呼び出しは 1 件も無い（<c>InputRouter</c>/<c>HookEngine</c> のコメントに
/// 「RegisterHotKey では処理できないキー」という言及はあるが、実際に呼ぶコードは存在しない）。
/// 実際の <c>HookEngine::hookThreadProc</c> は <c>mode == HookMode::LowLevel || mode == HookMode::Auto</c>
/// の場合に <b>無条件で</b> <c>WH_KEYBOARD_LL</c> と <c>WH_MOUSE_LL</c> の両方を有効化するだけであり、
/// <c>HookMode::SystemHotKey</c> の場合はどちらのフックも有効化しない（<c>RegisterHotKey</c> も
/// 呼ばれないため、事実上ホットキーが一切動作しないモードになる）。<c>HookEngine::selectHookStrategy</c> /
/// <c>requiresLowLevelHook</c> / <c>isMultimediaKey</c>（<see cref="InputRouter"/> とは別に
/// <c>HookEngine.h</c>/<c>.cpp</c> 自身が独自に持つ、ほぼ重複した実装）は定義されているが
/// <c>hookThreadProc</c> のどこからも呼ばれておらず、C++ 単体テストにも登場しない、実質的な
/// デッドコードであることを確認済み。そのため本クラスにはこれらの静的ヘルパーを移植しない
/// （<see cref="InputRouter"/> 側の同名メソッドが実際の C++ <c>InputRouter.cpp</c> の 1:1 移植として
/// 既に存在し、そちらは他の判定用途で正しく使われている）。
/// </para>
/// <para>
/// <b>専用スレッドを使う理由</b>: WH_KEYBOARD_LL/WH_MOUSE_LL は、インストールしたスレッドが
/// 活発にメッセージポンプを回し続けないと、Windows がレジストリ値 <c>LowLevelHooksTimeout</c>
/// （既定 300ms）を超えたと判断して黙ってフックを外してしまう（公式に文書化された挙動）。
/// もしメイン UI スレッドへ直接フックすると（<see cref="IMEIndicator.Services.KeyboardHook"/> は
/// この方式だが、あちらは監視専用でスレッド課題が軽微）、設定画面のモーダル表示中など UI
/// スレッドが一時的にメッセージポンプを離れる場面でグローバルホットキーが無効化されかねない。
/// C++ 版はこれを避けるため専用スレッド（<c>std::jthread</c> + 自前のメッセージループ）で
/// フックを保持しており、本移植でも同じ設計を踏襲する。
/// </para>
/// <para>
/// <b>コールバックの実際の経路</b>: フックプロシージャ自体は <c>PostMessageW</c> で
/// <paramref name="targetWnd"/>（呼び出し元アプリのメインウィンドウ）へ
/// <c>WM_HOTKEY_RAW_KBD</c>/<c>WM_HOTKEY_RAW_MOUSE</c> を投げるだけで、C++ 版ヘッダが宣言する
/// <c>HotkeyCallback callback</c> 引数は <c>start()</c> でメンバーに保存されるものの、
/// <c>HookEngine.cpp</c> のどこからも実際には呼び出されない（デッドフィールド）。実際の通知経路は
/// 投稿されたウィンドウメッセージを <paramref name="targetWnd"/> の所有者（<c>HotkeyService</c>、
/// T068）が自身のメッセージループ/WndProc で受け取り、<see cref="InputRouter.HandleRawHookMessage"/>
/// 経由でハンドラへ転送する形になる。本クラスも同じ設計とし、未使用のコールバック引数は
/// 持たない（未使用のまま持つと将来の読者を誤解させるため）。
/// </para>
/// </remarks>
public sealed class HookEngine
{
    // フック → メインウィンドウ通知用カスタム WM メッセージ。移植元 HookEngine.h の
    // WM_HOTKEY_RAW_KBD（= WM_APP + 0x100）/ WM_HOTKEY_RAW_MOUSE（= WM_APP + 0x101）と同値。
    // InputRouter.cs に同じ値の internal const が既にあるが、こちらはフック側の「送信元」定義として
    // 独立して保持する（InputRouter 側は「受信側」の定義。値は仕様上一致させる必要がある）。
    public const uint WM_HOTKEY_RAW_KBD = 0x8100;
    public const uint WM_HOTKEY_RAW_MOUSE = 0x8101;

    // フックコールバックからアクセスするための静的インスタンスポインタ。SetWindowsHookExW の
    // コールバックは this を運べないため、C++ 版 s_instance と同じ仕組みが必要
    // （契約: win32-interop-contract.md §コールバックの寿命と割り当て禁止。委譲関数オブジェクトを
    // 毎回 new せず、固定の static readonly デリゲートを使う）。
    private static volatile HookEngine? s_instance;

    private static readonly LowLevelKeyboardProc KeyboardProcDelegate = KeyboardProc;
    private static readonly LowLevelMouseProc MouseProcDelegate = MouseProc;

    private Thread? _thread;
    private volatile bool _stopRequested;
    private nint _targetWnd;
    private nint _kbHook;
    private nint _msHook;

    /// <summary>フックが動作中か。移植元 <c>isRunning()</c>。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// フックを開始する（呼び出し元スレッドではなく、内部で起動する専用スレッド上で
    /// SetWindowsHookExW を呼ぶ）。移植元 <c>start(HWND, HookMode, HotkeyCallback)</c>
    /// （<see cref="HookEngine"/> クラス doc コメントのとおり callback 引数は移植しない）。
    /// </summary>
    /// <param name="targetWnd">フック検出時に <c>WM_HOTKEY_RAW_KBD</c>/<c>WM_HOTKEY_RAW_MOUSE</c> を
    /// 投稿する先のウィンドウハンドル。</param>
    /// <param name="mode">フックモード。<see cref="HookMode.LowLevel"/>/<see cref="HookMode.Auto"/>
    /// のみ実際にフックを有効化する（クラス doc コメント参照）。</param>
    /// <returns>成功時 true。既に実行中の場合は false（移植元 <c>HookError::AlreadyRunning</c> 相当）。</returns>
    public bool Start(nint targetWnd, HookMode mode)
    {
        if (IsRunning)
        {
            Log.Hook.Warning("HookEngine.Start: 既に実行中です");
            return false;
        }

        _stopRequested = false;

        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => HookThreadProc(targetWnd, mode, ready))
        {
            IsBackground = true,
            Name = "IMEIndicator.HookEngine",
        };
        _thread.Start();

        // C++ 版 start() はスレッド起動後すぐ戻る（フック確立を待たない）が、C# 版では
        // 呼び出し元（HotkeyService.Start）が「フック確立の成否」を戻り値で正しく報告できるよう、
        // スレッド側でのフック設定完了（成功・失敗いずれも）を待ち合わせる（挙動をより頑健にした
        // 意図的な差異。フックの有無自体は変えない）。
        ready.Wait();

        IsRunning = true;
        Log.Hook.Information("HookEngine.Start: mode={Mode} kb={KbHook} mouse={MsHook}", mode, _kbHook != 0, _msHook != 0);
        return true;
    }

    /// <summary>フックを停止する（専用スレッドの終了を待つ）。移植元 <c>stop()</c>。</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _stopRequested = true;
        _thread?.Join();
        _thread = null;
        IsRunning = false;
        Log.Hook.Information("HookEngine.Stop");
    }

    // 専用スレッドのエントリポイント。移植元 hookThreadProc。
    private void HookThreadProc(nint targetWnd, HookMode mode, ManualResetEventSlim ready)
    {
        s_instance = this;
        _targetWnd = targetWnd;

        if (mode is HookMode.LowLevel or HookMode.Auto)
        {
            nint hModule = NativeMethods.GetModuleHandleW(0);
            _kbHook = NativeMethods.SetWindowsHookExW(NativeConstants.WH_KEYBOARD_LL, KeyboardProcDelegate, hModule, 0);
            _msHook = NativeMethods.SetWindowsHookExW(NativeConstants.WH_MOUSE_LL, MouseProcDelegate, hModule, 0);
        }

        ready.Set();

        // stop_token が要求されるまで MsgWaitForMultipleObjects でアイドル（移植元と同じ 100ms ポーリング）。
        var msg = default(MSG);
        while (!_stopRequested)
        {
            uint waitResult = NativeMethods.MsgWaitForMultipleObjects(0, 0, false, 100, NativeConstants.QS_ALLINPUT);
            if (waitResult == NativeConstants.WAIT_OBJECT_0)
            {
                while (NativeMethods.PeekMessageW(out msg, 0, 0, 0, NativeConstants.PM_REMOVE))
                {
                    NativeMethods.TranslateMessage(in msg);
                    NativeMethods.DispatchMessageW(in msg);
                }
            }
        }

        // フック解除。C++ 版は wil::unique_hhook のデストラクタ任せだが、C# 版は明示的に解除する。
        if (_kbHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_kbHook);
            _kbHook = 0;
        }

        if (_msHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_msHook);
            _msHook = 0;
        }

        s_instance = null;
        _targetWnd = 0;
    }

    // WH_KEYBOARD_LL 本体。フックプロシージャは最速で返す契約のため、PostMessageW のみ行う
    // （契約: win32-interop-contract.md §コールバックの寿命と割り当て禁止。new / ボックス化 / LINQ を
    // 行わない）。移植元 keyboardProc。
    private static unsafe nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        HookEngine? instance = s_instance;
        if (nCode == NativeConstants.HC_ACTION && instance is not null && instance._targetWnd != 0)
        {
            if (wParam == NativeConstants.WM_KEYDOWN || wParam == NativeConstants.WM_SYSKEYDOWN)
            {
                var kb = (KBDLLHOOKSTRUCT*)lParam;
                NativeMethods.PostMessageW(instance._targetWnd, WM_HOTKEY_RAW_KBD, kb->VkCode, (nint)kb->ScanCode);
            }
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    // WH_MOUSE_LL 本体。移植元 mouseProc。
    private static unsafe nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        HookEngine? instance = s_instance;
        if (nCode == NativeConstants.HC_ACTION && instance is not null && instance._targetWnd != 0)
        {
            if (wParam == NativeConstants.WM_LBUTTONDOWN || wParam == NativeConstants.WM_RBUTTONDOWN ||
                wParam == NativeConstants.WM_MBUTTONDOWN || wParam == NativeConstants.WM_XBUTTONDOWN ||
                wParam == NativeConstants.WM_MOUSEWHEEL || wParam == NativeConstants.WM_MOUSEHWHEEL)
            {
                var ms = (MSLLHOOKSTRUCT*)lParam;
                NativeMethods.PostMessageW(instance._targetWnd, WM_HOTKEY_RAW_MOUSE, (nuint)wParam, (nint)ms->MouseData);
            }
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }
}
