// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text;
using IMEIndicator.Interop;

namespace IMEIndicator.Services;

/// <summary>
/// ウィンドウ・プロセス名解決のヘルパー。移植元: src/cpp/win32/HwndUtil.h / .cpp のうち、
/// <c>HotkeyService</c>（T068）の multInst=false 判定が必要とする
/// <c>findWindowByExeName</c> / <c>bringWindowToFront</c> のみを移植する（<c>getWindowTitle</c> /
/// <c>getKeyboardLayoutOfHwnd</c> / <c>isJapaneseHkl</c> 等、C++ 版の他の用途向け関数は対象外）。
/// </summary>
internal static class HwndUtil
{
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<uint, (string Name, DateTime ExpiresAt)> ProcessNameCache = [];
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 指定した実行ファイル名（フルパスでも可、末尾のファイル名部分のみ比較）を持つプロセスの
    /// 可視トップレベルウィンドウを探す。移植元 <c>findWindowByExeName</c>。
    /// </summary>
    /// <param name="exeFullPathOrName">比較対象の実行ファイルパスまたはファイル名。</param>
    /// <returns>見つかった場合はそのウィンドウハンドル、無ければ 0。</returns>
    public static nint FindWindowByExeName(string exeFullPathOrName)
    {
        if (string.IsNullOrEmpty(exeFullPathOrName))
        {
            return 0;
        }

        string target = ExtractFileName(exeFullPathOrName).ToLowerInvariant();
        nint found = 0;

        bool EnumProc(nint hwnd, nint lParam)
        {
            // 表示可能なトップレベルウィンドウのみ対象（HotkeyP 元 findWindow 相当）。
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            if (NativeMethods.GetWindow(hwnd, NativeConstants.GW_OWNER) != 0)
            {
                return true; // 所有者ありはサブウィンドウ
            }

            if (NativeMethods.GetWindowTextLengthW(hwnd) == 0)
            {
                return true; // タイトル空はダイアログ等
            }

            string name = GetProcessNameByHwnd(hwnd);
            if (name.Length == 0)
            {
                return true;
            }

            if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false; // 列挙終了
            }

            return true;
        }

        NativeMethods.EnumWindows(EnumProc, 0);
        return found;
    }

    /// <summary>
    /// 指定ウィンドウを最小化解除・前面化する。フォアグラウンドロックの制約を
    /// <c>AttachThreadInput</c> による一時アタッチで回避する（HotkeyP 互換）。移植元 <c>bringWindowToFront</c>。
    /// </summary>
    public static bool BringWindowToFront(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeConstants.SW_RESTORE);
        }

        uint foreThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();

        if (foreThread != currentThread)
        {
            NativeMethods.AttachThreadInput(currentThread, foreThread, true);
        }

        if (targetThread != currentThread)
        {
            NativeMethods.AttachThreadInput(currentThread, targetThread, true);
        }

        NativeMethods.BringWindowToTop(hwnd);
        bool result = NativeMethods.SetForegroundWindow(hwnd);

        if (foreThread != currentThread)
        {
            NativeMethods.AttachThreadInput(currentThread, foreThread, false);
        }

        if (targetThread != currentThread)
        {
            NativeMethods.AttachThreadInput(currentThread, targetThread, false);
        }

        return result;
    }

    private static string GetProcessNameByHwnd(nint hwnd)
    {
        if (hwnd == 0)
        {
            return string.Empty;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return GetProcessNameByPid(pid);
    }

    // PID → 実行ファイル名（末尾部分のみ）。1 秒 TTL でキャッシュする（移植元と同じ）。
    // 1 回の FindWindowByExeName（EnumWindows）呼び出し中に同一プロセスの複数ウィンドウを
    // 引くケース（ブラウザ・IDE 等）で OpenProcess/QueryFullProcessImageNameW の呼び出しを
    // 減らすための最適化。ホットキー押下という同期経路で呼ばれるため、体感遅延を避ける目的。
    private static string GetProcessNameByPid(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        lock (CacheLock)
        {
            if (ProcessNameCache.TryGetValue(pid, out (string Name, DateTime ExpiresAt) cached))
            {
                if (DateTime.UtcNow < cached.ExpiresAt)
                {
                    return cached.Name;
                }

                ProcessNameCache.Remove(pid);
            }
        }

        nint hProcess = NativeMethods.OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == 0)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new StringBuilder(260); // MAX_PATH
            uint size = (uint)buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageNameW(hProcess, 0, buffer, ref size) || size == 0)
            {
                return string.Empty;
            }

            string name = ExtractFileName(buffer.ToString(0, (int)size));

            lock (CacheLock)
            {
                ProcessNameCache[pid] = (name, DateTime.UtcNow + CacheTtl);
            }

            return name;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static string ExtractFileName(string path)
    {
        int slash = path.LastIndexOfAny(['\\', '/']);
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
