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
/// <param name="ReliableStatus">IME 状態 API（<see cref="ImeDetector.GetImeOpenStatusEx"/>）の取得に成功したか。</param>
public readonly record struct ImeDetectionResult(LanguageInfo State, bool ReliableStatus);

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
    /// IMM32 経由の三段判定で IME ON/OFF を取得する。移植元: IMEDetector::getIMEOpenStatusEx。
    /// <list type="number">
    /// <item>ImmGetDefaultIMEWnd → SendMessageTimeout(WM_IME_CONTROL, IMC_GETOPENSTATUS)</item>
    /// <item>ImmGetContext → ImmGetOpenStatus（<paramref name="hwndForeground"/>）</item>
    /// <item><paramref name="hwndFocus"/> でも試行</item>
    /// </list>
    /// </summary>
    /// <param name="hwndFocus">GetGUIThreadInfo 等で得たフォーカスウィンドウ。</param>
    /// <param name="hwndForeground">GetForegroundWindow で得たフォアグラウンドウィンドウ。</param>
    /// <returns>IsOpen: IME が ON か。Success: いずれかの方法で API 取得に成功したか。</returns>
    public static (bool IsOpen, bool Success) GetImeOpenStatusEx(nint hwndFocus, nint hwndForeground)
    {
        // 方法 1: DefaultIMEWnd に WM_IME_CONTROL(IMC_GETOPENSTATUS) を投げる。
        // SendMessageTimeout(SMTO_ABORTIFHUNG, 100ms) で応答無しウィンドウを避ける。
        // 定数は NativeConstants.WM_IME_CONTROL / IMC_GETOPENSTATUS を再利用する
        // （C++ 版は局所定数で複製しているが、C# 版は共通定数を既に持つため単一情報源にする）。
        nint imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwndForeground);
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

        // 方法 2: ImmGetContext → ImmGetOpenStatus（hwndForeground）
        nint hImcForeground = NativeMethods.ImmGetContext(hwndForeground);
        if (hImcForeground != 0)
        {
            bool isOpen = NativeMethods.ImmGetOpenStatus(hImcForeground);
            NativeMethods.ImmReleaseContext(hwndForeground, hImcForeground);
            return (isOpen, true);
        }

        // 方法 3: hwndFocus でも試行
        if (hwndFocus != 0 && hwndFocus != hwndForeground)
        {
            nint hImcFocus = NativeMethods.ImmGetContext(hwndFocus);
            if (hImcFocus != 0)
            {
                bool isOpen = NativeMethods.ImmGetOpenStatus(hImcFocus);
                NativeMethods.ImmReleaseContext(hwndFocus, hImcFocus);
                return (isOpen, true);
            }
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
            return new ImeDetectionResult(new LanguageInfo(LanguageType.English, false), false);
        }

        // pid・threadId は同じ GetWindowThreadProcessId 呼び出しから得る
        // （C++ 版は getProcessNameByHwnd 内部と外側で 2 回呼ぶが、結果は同一なので 1 回に統合する）。
        uint threadId = NativeMethods.GetWindowThreadProcessId(hwndForeground, out uint pid);
        string processName = GetProcessNameNoExt(pid);

        nint hwndTarget = hwndForeground;
        uint focusThreadId = threadId;

        GUITHREADINFO guiInfo = GUITHREADINFO.Create();
        if (NativeMethods.GetGUIThreadInfo(threadId, ref guiInfo))
        {
            if (guiInfo.HwndFocus != 0 && guiInfo.HwndFocus != hwndForeground)
            {
                hwndTarget = guiInfo.HwndFocus;
                focusThreadId = NativeMethods.GetWindowThreadProcessId(hwndTarget, out _);
            }
        }

        nint hkl = NativeMethods.GetKeyboardLayout(focusThreadId);
        int langId = (int)(hkl & 0xFFFF);

        (bool imeOpen, bool imeSuccess) = GetImeOpenStatusEx(hwndTarget, hwndForeground);

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

        return new ImeDetectionResult(new LanguageInfo(language, imeOpen), imeSuccess);
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
