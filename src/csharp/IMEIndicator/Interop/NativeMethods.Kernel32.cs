// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;

namespace IMEIndicator.Interop;

// kernel32.dll。
internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint hMem);

    // GetSystemCpuSetInformation は Windows 10 以降のみ export される（E-Core 判定、ECoreCpuInfo）。
    // Windows 11 専用アプリのため常に export されているが、C++ 版に合わせ GetProcAddress で動的解決する
    // （静的な DllImport/LibraryImport で束縛すると旧環境で DllNotFoundException/EntryPointNotFoundException
    //   が型ロード時に評価される場合があるため、実行時解決で ECoreCpuInfo 単体の失敗に閉じ込める）。
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string lpModuleName);

    // 上のオーバーロードは string?（NULL）を受け付けないため、呼び出し元モジュール自身のハンドルを
    // 取得する用途向けに nint 版を追加する。KeyboardHook.Start() の
    // SetWindowsHookExW(WH_KEYBOARD_LL, ..., hMod, 0) で、現行 C++ 版の ::GetModuleHandleW(nullptr)
    // と同じ意味（NULL = 呼び出し元プロセスの実行ファイルを含むモジュール）で使う。
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandleW(nint lpModuleName);

    // GetProcAddress のエクスポート名は常に ASCII（UTF-8 と同一バイト列になる）
    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint hModule, string lpProcName);

    // ProcessPriorityService.IsAccessibleForControl 専用（T037）。
    // .NET の Process クラスには「書き込み権限だけを試す」直接 API が無いため、
    // 現行 C++ 版（OpenProcess(PROCESS_SET_INFORMATION, ...)）と同じ判定を行うために使う。
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    // HotkeyService.BringWindowToFront（AttachThreadInput でフォアグラウンドロックを回避する）専用。
    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    // HwndUtil.GetProcessNameByPid 専用。PROCESS_QUERY_LIMITED_INFORMATION ハンドルでも取得できる、
    // 昇格プロセスとの権限境界を跨いでも失敗しにくい低権限 API
    // （GetModuleFileNameEx や Process.MainModule より広い権限境界で動く）。
    // StringBuilder 引数は [LibraryImport] のソース生成でサポートされない（SYSLIB1051）ため、
    // GetMonitorInfoW 等と同じく classic [DllImport] にフォールバックする。
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);
}
