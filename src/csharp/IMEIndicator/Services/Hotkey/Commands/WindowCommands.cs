// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// ウィンドウ操作系の内部コマンド実装（内部コマンド ID 7 / 8 / 9 / 10 / 25 / 28 / 65 / 77 / 81 /
/// 83 / 84 / 90 / 101 / 102 / 110 / 111 / 112 / 115。contracts/internal-command-catalog.md
/// 「ウィンドウ（18）」）。移植元: src/cpp/services/hotkey/commands/WindowCommands.h / .cpp。
/// </summary>
/// <remarks>
/// <para>
/// ID → メソッドの dispatch は本クラスの責務外（別タスクの CommandExecutor.cs が担当する）。
/// 対応表は C++ 版 <c>CommandExecutor.cpp::executeCommandById</c> の「ウィンドウコマンド」ブロック
/// （case 7〜115、s_mainHwnd を参照する箇所を含む）を根拠とする。
/// </para>
/// <para>
/// <b>重要</b>: <c>WindowCommands.cpp</c> 末尾の <c>executeWindowCommand(int cmdId, std::wstring_view,
/// HWND)</c> は、宣言・定義以外に呼び出し箇所が無いデッドコード（CommandExecutor.cpp はこの関数を
/// 経由せず、自身の switch 文で個々の関数を直接呼ぶ）。その switch 文の ID 対応は実際に使われる
/// CommandExecutor.cpp の対応と食い違う（例: executeWindowCommand は case 7 を
/// <c>minimizeActiveWindow()</c>、case 8 を <c>maximizeActiveWindow()</c> に割り当てるが、
/// CommandExecutor.cpp では逆に case 7 が <c>maximizeActiveWindow()</c>、case 8 が
/// <c>minimizeActiveWindow()</c>）。本クラスの各メソッドと ID の対応は必ず CommandExecutor.cpp 側を
/// 正としており、executeWindowCommand は一切参照していない。
/// </para>
/// <para>
/// 表示名と実際の動作が食い違う既知のケース（internal-command-catalog.md に ⚠ が付く 6 件。
/// 是正はせず現行実装どおり移植する）:
/// ID 25「ウィンドウ: 非表示」→ 実際は <see cref="ToggleAlwaysOnTop"/>。
/// ID 65「ウィンドウ: 他のウィンドウを最小化」→ 実際は <see cref="MinimizeToTray"/>
/// （フォアグラウンドウィンドウではなく自アプリのメインウィンドウを最小化）。
/// ID 83「テキスト: 前のタスク（Alt+Shift+Tab）」→ 実際は <see cref="SwitchToNextWindow"/> と同じ
/// （Shift は送出せず、ID 84 と全く同一の Alt+Tab）。
/// ID 90「ディスプレイ: ウィンドウスクリーンショット」→ 実際は <see cref="CenterWindow"/>。
/// ID 110「ウィンドウ: 不透明度 +」→ 実際は前面ウィンドウの alpha を 10 減らす（より透明に）。
/// ID 111「ウィンドウ: 不透明度 -」→ 実際は alpha を 10 増やす（より不透明に）。
/// 110/111 は <see cref="AdjustForegroundOpacityBy"/> 経由で呼ぶ想定。
/// </para>
/// <para>
/// 本クラスの担当外（本タスク T058 の明示的なスコープ外）: ID 29〜36（画面端配置スナップ、
/// 「SnapToRegion」）は internal-command-catalog.md 上は本クラス（WindowCommands.cs）が担当する
/// 分類として記載されているが、本タスクの実施時点ではウィンドウスナップ専用の実装として
/// CommandExecutor.cs 側で別途扱う方針が指示されたため、本クラスにはメソッドを設けていない。
/// カタログ文書 / tasks.md の記載（「画面端配置（8） | Commands/WindowCommands.cs（SnapToRegion）」）
/// とこの実装は現時点で乖離しているため、後続工程で整合を取る際は要確認。
/// </para>
/// </remarks>
public static class WindowCommands
{
    // ---- WM_CLOSE / WM_COMMAND（CloseActiveWindow / ShowDesktop 専用）----
    // https://learn.microsoft.com/windows/win32/api/winuser/wm-close
    private const int WmClose = 0x0010;
    // https://learn.microsoft.com/windows/win32/api/winuser/wm-command
    private const int WmCommand = 0x0111;
    // Shell_TrayWnd が受け取る「デスクトップの表示」トグルの内部コマンド ID（Explorer の非公開実装
    // 依存。公式ドキュメント化されていないが HotkeyP 由来でよく知られた値。C++ 版 showDesktop() と同じ）。
    private const nuint ShellShowDesktopCommandId = 407;

    // ---- GetWindowLongPtrW/SetWindowLongPtrW（ToggleAlwaysOnTop / SetOpacity 専用）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowlongptrw
    private const int GwlExStyle = -20;
    // 拡張ウィンドウスタイル（winuser.h）。
    private const int WsExTopmost = 0x00000008;

    // ---- SetLayeredWindowAttributes（SetOpacity 専用）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes
    private const uint LwaAlpha = 0x00000002;

    /// <summary>
    /// アクティブウィンドウを最小化（<c>minimizeActiveWindow()</c> 相当）。
    /// 内部コマンド ID 8・101（101 は表示名「アプリ非表示」だが実際は単なる最小化）。
    /// </summary>
    /// <returns>成功時は null。対象ウィンドウが無い場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? MinimizeActiveWindow()
    {
        nint hw = GetForegroundOrNull();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        NativeMethods.ShowWindow(hw, NativeConstants.SW_MINIMIZE);
        return null;
    }

    /// <summary>
    /// アクティブウィンドウを最大化/元のサイズに戻す（トグル、<c>maximizeActiveWindow()</c> 相当）。
    /// 内部コマンド ID 7。
    /// </summary>
    /// <returns>
    /// 成功時は null。対象ウィンドウが無い場合、または <c>GetWindowPlacement</c> が失敗した場合は
    /// <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? MaximizeActiveWindow()
    {
        nint hw = GetForegroundOrNull();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        WINDOWPLACEMENT wp = WINDOWPLACEMENT.Create();
        if (!NativeMethods.GetWindowPlacement(hw, ref wp))
        {
            return ExecuteError.ApiCallFailed;
        }

        // 最大化中なら元に戻す、そうでなければ最大化（C++ 版と同じ条件）。
        NativeMethods.ShowWindow(
            hw,
            wp.ShowCmd == NativeConstants.SW_SHOWMAXIMIZED ? NativeConstants.SW_RESTORE : NativeConstants.SW_MAXIMIZE);
        return null;
    }

    /// <summary>
    /// アクティブウィンドウを閉じる（<c>closeActiveWindow()</c> 相当、WM_CLOSE を送出）。
    /// 内部コマンド ID 9。
    /// </summary>
    /// <returns>成功時は null。対象ウィンドウが無い場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? CloseActiveWindow()
    {
        nint hw = GetForegroundOrNull();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        // PostMessageW の戻り値は確認しない（C++ 版と同じ。WM_CLOSE の配送自体は
        // 非同期でありここでは結果を待たない）。
        NativeMethods.PostMessageW(hw, WmClose, 0, 0);
        return null;
    }

    /// <summary>
    /// 自アプリ（IMEIndicator）のメインウィンドウを最小化してトレイに格納する
    /// （<c>minimizeToTray(HWND mainHwnd)</c> 相当）。フォアグラウンドウィンドウではなく、
    /// 呼び出し元が渡すメインウィンドウハンドルが対象。内部コマンド ID 65・102・115
    /// （表示名は異なるがいずれも同じ動作）。
    /// </summary>
    /// <param name="mainHwnd">IMEIndicator 自身のメインウィンドウハンドル。呼び出し元が渡す。</param>
    /// <returns>成功時は null。<paramref name="mainHwnd"/> が無効（0）な場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? MinimizeToTray(nint mainHwnd)
    {
        if (mainHwnd == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        NativeMethods.ShowWindow(mainHwnd, NativeConstants.SW_MINIMIZE);
        return null;
    }

    /// <summary>
    /// デスクトップを表示する（<c>showDesktop()</c> 相当）。Shell_TrayWnd へ
    /// WM_COMMAND 407 を送って Explorer の「デスクトップの表示」を呼び出す。内部コマンド ID 81。
    /// </summary>
    /// <returns>成功時は null。Shell_TrayWnd が見つからない場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? ShowDesktop()
    {
        nint trayWnd = NativeMethods.FindWindowW("Shell_TrayWnd", null);
        if (trayWnd == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        NativeMethods.SendMessageW(trayWnd, WmCommand, ShellShowDesktopCommandId, 0);
        return null;
    }

    /// <summary>
    /// アクティブウィンドウを常に手前に表示するかどうかをトグルする
    /// （<c>toggleAlwaysOnTop()</c> 相当）。内部コマンド ID 10・25（25 は表示名「非表示」だが
    /// 実際は本メソッドと同じ常に手前トグル）。
    /// </summary>
    /// <returns>成功時は null。対象ウィンドウが無い場合は <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? ToggleAlwaysOnTop()
    {
        nint hw = GetForegroundOrNull();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        int exStyle = NativeMethods.GetWindowLongPtrW(hw, GwlExStyle);
        bool isTopmost = (exStyle & WsExTopmost) != 0;

        // SetWindowPos の戻り値は確認しない（C++ 版と同じ）。
        NativeMethods.SetWindowPos(
            hw,
            isTopmost ? NativeConstants.HWND_NOTOPMOST : NativeConstants.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE);
        return null;
    }

    /// <summary>
    /// アクティブウィンドウの不透明度を設定する（<c>setWindowOpacity(int opacity)</c> 相当）。
    /// 内部コマンド ID 77（未指定時は param のパース失敗として呼び出し元が既定値 128 を渡す想定）。
    /// </summary>
    /// <param name="opacity">
    /// 0〜255 の透明度パラメータ（範囲外は clamp する）。<b>alpha 値そのものではない</b>点に注意:
    /// 0 は「完全不透明」を表す特殊値として扱い alpha=255 にする（HotkeyP 由来の慣例）。
    /// それ以外は alpha = 255 - opacity（値が大きいほど透明になる）。
    /// </param>
    /// <returns>
    /// 成功時は null。対象ウィンドウが無い場合、または <c>SetLayeredWindowAttributes</c> が失敗した
    /// 場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? SetOpacity(int opacity)
    {
        nint hw = GetForegroundOrNull();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        int clamped = Math.Clamp(opacity, 0, 255);

        // WS_EX_LAYERED が必要（未設定なら付与する。C++ 版と同じ）。
        int exStyle = NativeMethods.GetWindowLongPtrW(hw, GwlExStyle);
        if ((exStyle & NativeConstants.WS_EX_LAYERED) == 0)
        {
            NativeMethods.SetWindowLongPtrW(hw, GwlExStyle, exStyle | NativeConstants.WS_EX_LAYERED);
        }

        // opacity=0 は完全不透明（255）として扱う（HotkeyP の慣例。C++ 版と同じ）。
        byte alpha = clamped == 0 ? (byte)255 : (byte)(255 - clamped);
        if (!NativeMethods.SetLayeredWindowAttributes(hw, 0, alpha, LwaAlpha))
        {
            return ExecuteError.ApiCallFailed;
        }
        return null;
    }

    /// <summary>
    /// アクティブウィンドウを、それが属するモニターの作業領域の中央に移動する
    /// （<c>centerWindow()</c> 相当）。内部コマンド ID 28・90（90 は表示名
    /// 「ウィンドウスクリーンショット」だが実際は本メソッドと同じ中央配置）。
    /// </summary>
    /// <returns>
    /// 成功時は null。対象ウィンドウが無い場合、または <c>GetWindowRect</c>/<c>GetMonitorInfoW</c> が
    /// 失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? CenterWindow()
    {
        nint hw = GetForegroundOrNull();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        if (!NativeMethods.GetWindowRect(hw, out RECT wndRect))
        {
            return ExecuteError.ApiCallFailed;
        }

        // ウィンドウが属するモニターを取得（既存の MONITORINFOEXW 版 GetMonitorInfoW を再利用。
        // C++ 版は device name を持たない MONITORINFO を使うが、rcWork の先頭レイアウトは同一で
        // 取得結果に影響しない）。
        nint hmon = NativeMethods.MonitorFromWindow(hw, NativeConstants.MONITOR_DEFAULTTONEAREST);
        MONITORINFOEXW mi = MONITORINFOEXW.Create();
        if (!NativeMethods.GetMonitorInfoW(hmon, ref mi))
        {
            return ExecuteError.ApiCallFailed;
        }

        int wndW = wndRect.Width;
        int wndH = wndRect.Height;
        int monW = mi.RcWork.Width;
        int monH = mi.RcWork.Height;

        int x = mi.RcWork.Left + ((monW - wndW) / 2);
        int y = mi.RcWork.Top + ((monH - wndH) / 2);

        // SetWindowPos の戻り値は確認しない（C++ 版と同じ）。
        NativeMethods.SetWindowPos(hw, 0, x, y, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER);
        return null;
    }

    /// <summary>
    /// 次のウィンドウにフォーカスを移動する（Alt+Tab 相当、<c>switchToNextWindow()</c> 相当）。
    /// keybd_event で Alt Down → Tab Down → Tab Up → Alt Up の順に送出する。
    /// 内部コマンド ID 83・84（83 は表示名「前のタスク（Alt+Shift+Tab）」だが Shift は送出せず、
    /// 84 と全く同じ動作）。
    /// </summary>
    /// <returns>常に null（C++ 版も keybd_event の結果を確認せず常に成功として扱う）。</returns>
    public static ExecuteError? SwitchToNextWindow()
    {
        NativeMethods.keybd_event((byte)NativeConstants.VK_MENU, 0, 0, 0);
        NativeMethods.keybd_event((byte)NativeConstants.VK_TAB, 0, 0, 0);
        NativeMethods.keybd_event((byte)NativeConstants.VK_TAB, 0, NativeConstants.KEYEVENTF_KEYUP, 0);
        NativeMethods.keybd_event((byte)NativeConstants.VK_MENU, 0, NativeConstants.KEYEVENTF_KEYUP, 0);
        return null;
    }

    /// <summary>
    /// 前面ウィンドウの現在の alpha を取得し、<paramref name="delta"/> を加えた値を
    /// <see cref="SetOpacity"/> に渡す。内部コマンド ID 110（<paramref name="delta"/>=-10 で呼ぶ想定。
    /// 表示名は「不透明度 +」だが実際は alpha を減らして透明にする）・111
    /// （<paramref name="delta"/>=+10 で呼ぶ想定。表示名は「不透明度 -」だが実際は alpha を増やして
    /// 不透明にする）。
    /// </summary>
    /// <remarks>
    /// C++ 版 CommandExecutor.cpp の該当 case は、他のメソッドが使う <c>getForegroundOrNull()</c>
    /// ではなく <c>GetForegroundWindow()</c> を直接呼んでおり、デスクトップの除外を行わない
    /// （既知の差異だが、修正せず C++ 版の実装どおり忠実に再現する）。
    /// また C++ 版は <c>BYTE alpha = 255;</c> を初期値にしたまま <c>GetLayeredWindowAttributes</c>
    /// の戻り値を確認せず使う（対象がレイヤードウィンドウでなく API が失敗した場合、alpha は
    /// 初期値の 255 のまま使われる）。C# の <c>out</c> 引数はネイティブ側が書き込まなければ既定値の
    /// 0 になり、C++ のような「呼び出し前に代入した値がそのまま残る」フォールバックは成立しない
    /// （<see cref="DisplayHelper"/> の DPI 取得と同じ注意点）ため、ここでは戻り値を確認して
    /// 明示的に 255 へフォールバックする。
    /// </remarks>
    /// <param name="delta">alpha に加算する差分。110 用に -10、111 用に +10 を渡す想定。</param>
    /// <returns>
    /// 成功時は null。対象ウィンドウが無い場合、または内部で呼ぶ <see cref="SetOpacity"/> が失敗した
    /// 場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? AdjustForegroundOpacityBy(int delta)
    {
        nint hw = NativeMethods.GetForegroundWindow();
        if (hw == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        bool gotAlpha = NativeMethods.GetLayeredWindowAttributes(hw, out _, out byte currentAlpha, out _);
        byte alpha = gotAlpha ? currentAlpha : (byte)255;

        // SetOpacity（= C++ 版 setWindowOpacity）の引数は alpha 値そのものではなく「透明度
        // パラメータ」だが、C++ 版 case 110/111 は現在の alpha を土台にした値をそのまま
        // setWindowOpacity に渡している。その計算式をそのまま踏襲する。
        int newParam = delta < 0
            ? Math.Max(0, alpha + delta)
            : Math.Min(255, alpha + delta);

        return SetOpacity(newParam);
    }

    /// <summary>
    /// 可視かつ最小化されていない全トップレベルウィンドウを最大化する。内部コマンド ID 112。
    /// </summary>
    /// <remarks>
    /// C++ 版ではこの列挙ロジックが CommandExecutor.cpp の case 112 に直接書かれており、
    /// WindowCommands.h/.cpp に対応する独立関数は無い（他のメソッドと異なり 1:1 の移植元関数が
    /// 存在しないため、本クラスで新設した）。
    /// </remarks>
    /// <returns>常に null（C++ 版も EnumWindows の結果を確認せず常に成功として扱う）。</returns>
    public static ExecuteError? MaximizeAll()
    {
        static bool EnumProc(nint hwnd, nint lParam)
        {
            if (NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeConstants.SW_MAXIMIZE);
            }
            return true;
        }

        NativeMethods.EnumWindows(EnumProc, 0);
        return null;
    }

    /// <summary>
    /// 現在のフォアグラウンドウィンドウを返す。デスクトップ自体がフォアグラウンドの場合は
    /// 対象ウィンドウ無し（0）として扱う（C++ 版 <c>getForegroundOrNull()</c> と同じ）。
    /// <see cref="AdjustForegroundOpacityBy"/>（ID 110/111）はこのヘルパーを使わず
    /// <c>GetForegroundWindow()</c> を直接呼ぶ。理由は同メソッドの <c>remarks</c> を参照。
    /// </summary>
    private static nint GetForegroundOrNull()
    {
        nint hw = NativeMethods.GetForegroundWindow();
        if (hw == 0)
        {
            return 0;
        }

        nint desktop = NativeMethods.GetDesktopWindow();
        return hw == desktop ? 0 : hw;
    }
}
