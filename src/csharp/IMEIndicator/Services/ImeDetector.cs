// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;

using IMEIndicator.Interop;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// IME 検出の結果。<see cref="State"/> は最新の言語/ON-OFF、<see cref="ReliableStatus"/> は
/// API 取得が成功したかどうか。移植元: src/cpp/services/IMEDetector.h の IMEDetectionResult 構造体。
/// </summary>
/// <param name="State">現在の入力言語と IME の ON/OFF。</param>
/// <param name="ReliableStatus">
/// IME 状態 API（<see cref="ImeDetector.GetImeOpenStatusEx"/>）が <see cref="QueryWindow"/> に対して
/// 取得に成功したか。<see langword="false"/> のとき <see cref="State"/>.IsImeOn は判定に使わないこと
/// （specs/017-fix-notepad-ime-display/data-model.md §1）。
/// </param>
/// <param name="ForegroundWindow">読み取り時の前面ウィンドウ（<c>GetForegroundWindow</c>）。診断ログ用（FR-011）。</param>
/// <param name="ForegroundThreadId">前面ウィンドウのスレッド。診断ログ用。</param>
/// <param name="QueryWindow">
/// 実際に IME 状態を問い合わせたウィンドウ（フォーカスのウィンドウ、得られなければ前面ウィンドウ。
/// <see cref="ImeStateRules.SelectQueryWindow"/>）。診断ログ用。
/// </param>
/// <param name="QueryThreadId">問い合わせ先ウィンドウのスレッド（入力言語の判定にも使う）。診断ログ用。</param>
public readonly record struct ImeDetectionResult(
    LanguageInfo State,
    bool ReliableStatus,
    nint ForegroundWindow,
    uint ForegroundThreadId,
    nint QueryWindow,
    uint QueryThreadId);

/// <summary>
/// IME 状態検出（IMM32 経由 + フォアグラウンドウィンドウ追跡）。
/// 移植元: src/cpp/services/IMEDetector.h / .cpp（IMEDetector 構造体）と等価動作。
/// PixelIMEDetector との二段検出フローは ImeMonitor（T027、別タスク）側で組み合わせる。
/// </summary>
public static class ImeDetector
{
    // 言語 ID（HKL 下位 16 ビット）。0x0411 = 日本語(ja-JP)。移植元 IMEDetector.cpp の kLangJapanese。
    private const int LangJapanese = 0x0411;

    // 移植元 IMEDetector.cpp 無名名前空間の kTerminalProcesses と同一集合
    // （拡張子なし、大小無視で比較）。IME 状態取得が信頼できないターミナル系プロセス名。
    private static readonly string[] TerminalProcesses =
    [
        "powershell",
        "pwsh",
        "cmd",
        "WindowsTerminal",
        "conhost",
        "wezterm-gui",
        "alacritty",
        "mintty",
    ];

    /// <summary>
    /// IME 状態取得が信頼できないターミナル系プロセスか判定する（拡張子なし、大小無視）。
    /// 移植元: IMEDetector::isTerminalProcess。
    /// </summary>
    public static bool IsTerminalProcess(string processNameNoExt)
    {
        foreach (string candidate in TerminalProcesses)
        {
            if (string.Equals(processNameNoExt, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 言語 ID（HKL 下位 16 ビット）から <see cref="LanguageType"/> を判定する。
    /// 0x0411 = 日本語、それ以外は英語。移植元: IMEDetector::getLanguageType。
    /// </summary>
    public static LanguageType GetLanguageType(int langId) =>
        langId == LangJapanese ? LanguageType.Japanese : LanguageType.English;

    /// <summary>
    /// IMM32 経由の二段判定で、<paramref name="queryWindow"/> の IME ON/OFF を取得する。
    /// 移植元: IMEDetector::getIMEOpenStatusEx。
    /// <list type="number">
    /// <item>ImmGetDefaultIMEWnd(<paramref name="queryWindow"/>) → SendMessageTimeout(WM_IME_CONTROL, IMC_GETOPENSTATUS)</item>
    /// <item>ImmGetContext(<paramref name="queryWindow"/>) → ImmGetOpenStatus</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 017-fix-notepad-ime-display での変更点: 旧実装は方法 1・2 を常に前面ウィンドウへ、
    /// フォーカスのウィンドウは方法 3 の最終手段としてしか使わなかった。しかし Windows 11 の
    /// メモ帳は本文の編集コントロール（RichEditD2DPT）がトップレベルウィンドウとは別スレッドで
    /// 動作するため、方法 1 が前面（トップレベル）ウィンドウに対して「成功（非 0）」を返しつつ、
    /// そのスレッドの IME コンテキストは常に閉じている（値 0）ことを実測で確認した
    /// （specs/017-fix-notepad-ime-display/research.md R-1「実測結果 §1・§3」）。
    /// 成功と判定された時点で方法 2・3 は試みられないため、常に誤った OFF が返っていた。
    /// </para>
    /// <para>
    /// 本メソッドは呼び出し元（<see cref="GetCurrentImeStateEx"/>）が
    /// <see cref="ImeStateRules.SelectQueryWindow"/> で選んだ 1 つの問い合わせ先だけに対して
    /// 両方法を試す。呼び出し元がフォーカスのウィンドウを渡して失敗しても、このメソッド内で
    /// 前面ウィンドウへ問い合わせ直すことはしない（別スレッドの値を成功扱いにすると、
    /// 本不具合と同じ誤り方になるため。research.md R-1「禁止」節）。
    /// IME はフォーカスのあるスレッドの入力コンテキストに対して動作するため、
    /// 問い合わせ先をそこに揃えるのが一般則として正しい
    /// （<see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguithreadinfo">GetGUIThreadInfo</see>）。
    /// </para>
    /// </remarks>
    /// <param name="queryWindow">
    /// IME 状態を問い合わせるウィンドウ（<see cref="ImeStateRules.SelectQueryWindow"/> が選んだもの）。
    /// </param>
    /// <returns>IsOpen: IME が ON か。Success: いずれかの方法で API 取得に成功したか。</returns>
    public static (bool IsOpen, bool Success) GetImeOpenStatusEx(nint queryWindow)
    {
        // 方法 1: DefaultIMEWnd に WM_IME_CONTROL(IMC_GETOPENSTATUS) を投げる。
        // SendMessageTimeout(SMTO_ABORTIFHUNG, 100ms) で応答無しウィンドウを避ける。
        // 定数は NativeConstants.WM_IME_CONTROL / IMC_GETOPENSTATUS を再利用する
        // （C++ 版は局所定数で複製しているが、C# 版は共通定数を既に持つため単一情報源にする）。
        nint imeWnd = NativeMethods.ImmGetDefaultIMEWnd(queryWindow);
        if (imeWnd != 0)
        {
            nint sendResult = NativeMethods.SendMessageTimeoutW(
                imeWnd,
                (uint)NativeConstants.WM_IME_CONTROL,
                (nuint)NativeConstants.IMC_GETOPENSTATUS,
                0,
                NativeConstants.SMTO_ABORTIFHUNG,
                100,
                out nuint result);
            if (sendResult != 0)
            {
                return (result != 0, true);
            }
        }

        // 方法 2: ImmGetContext → ImmGetOpenStatus（同じ queryWindow に対して）
        nint hImc = NativeMethods.ImmGetContext(queryWindow);
        if (hImc != 0)
        {
            bool isOpen = NativeMethods.ImmGetOpenStatus(hImc);
            NativeMethods.ImmReleaseContext(queryWindow, hImc);
            return (isOpen, true);
        }

        return (false, false);
    }

    /// <summary>
    /// 現在の IME 状態（フォアグラウンドウィンドウのキーボードレイアウト + IME ON/OFF）を取得する。
    /// ターミナルプロセスの場合、<paramref name="trackedLanguageForTerminal"/> が指定されていれば
    /// その言語を採用する。移植元: IMEDetector::getCurrentIMEStateEx。
    /// </summary>
    /// <param name="trackedLanguageForTerminal">
    /// ターミナル系プロセス上で採用する追跡済み言語（C++ 版 std::optional 相当）。
    /// </param>
    public static ImeDetectionResult GetCurrentImeStateEx(LanguageType? trackedLanguageForTerminal)
    {
        nint hwndForeground = NativeMethods.GetForegroundWindow();
        if (hwndForeground == 0)
        {
            return new ImeDetectionResult(new LanguageInfo(LanguageType.English, false), false, 0, 0, 0, 0);
        }

        // pid・threadId は同じ GetWindowThreadProcessId 呼び出しから得る
        // （C++ 版は getProcessNameByHwnd 内部と外側で 2 回呼ぶが、結果は同一なので 1 回に統合する）。
        uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(hwndForeground, out uint pid);
        string processName = GetProcessNameNoExt(pid);

        // GetGUIThreadInfo が失敗、またはフォーカスが得られない場合は focusWindow = 0 とし、
        // ImeStateRules.SelectQueryWindow が前面ウィンドウへフォールバックする
        // （research.md R-1「問い合わせ先の決定規則」）。
        GUITHREADINFO guiInfo = GUITHREADINFO.Create();
        nint focusWindow = NativeMethods.GetGUIThreadInfo(foregroundThreadId, ref guiInfo) ? guiInfo.HwndFocus : 0;

        nint queryWindow = ImeStateRules.SelectQueryWindow(hwndForeground, focusWindow);
        uint queryThreadId = queryWindow == hwndForeground
            ? foregroundThreadId
            : NativeMethods.GetWindowThreadProcessId(queryWindow, out _);

        // 入力言語の判定も問い合わせ先のスレッドで行う（旧実装は「フォーカスが前面と異なる場合のみ
        // フォーカス側の HKL を見る」という条件付きだったが、SelectQueryWindow が既に同じ判断をしているため、
        // ここでは常に queryThreadId を使えばよい。結果は既存と同じになる）。
        nint hkl = NativeMethods.GetKeyboardLayout(queryThreadId);
        int langId = (int)(hkl & 0xFFFF);

        (bool imeOpen, bool imeSuccess) = GetImeOpenStatusEx(queryWindow);

        LanguageType language = GetLanguageType(langId);
        if (IsTerminalProcess(processName) && trackedLanguageForTerminal.HasValue)
        {
            language = trackedLanguageForTerminal.Value;
        }

        // 日本語以外は IME OFF と同じ表示にする（既存 C# 版互換）。
        if (language != LanguageType.Japanese)
        {
            imeOpen = false;
        }

        return new ImeDetectionResult(
            new LanguageInfo(language, imeOpen), imeSuccess, hwndForeground, foregroundThreadId, queryWindow, queryThreadId);
    }

    /// <summary>
    /// HWND を所有するプロセスの実行ファイル名（拡張子なし）を取得する。
    /// 移植元 win32::getProcessNameByHwnd + stripExeExtension の代替として
    /// <see cref="Process.GetProcessById(int)"/>.<see cref="Process.ProcessName"/> を使う
    /// （ProcessName は元々拡張子を含まないため、別途ストリップ処理は不要）。
    /// 取得失敗（プロセス終了・アクセス拒否等）時は空文字を返す（C++ 版の noexcept 相当の耐性）。
    /// </summary>
    private static string GetProcessNameNoExt(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
