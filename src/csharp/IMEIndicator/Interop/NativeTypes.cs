// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;

namespace IMEIndicator.Interop;

// 契約: specs/014-port-to-csharp/contracts/win32-interop-contract.md
// Win32 の構造体・定数・delegate を集約する。値は現行 C++ 版（src/cpp/win32/NativeConstants.h、
// 各 services/*.cpp）および Win32 API の公式ドキュメント（learn.microsoft.com）に基づく。

#region 構造体

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int CX;
    public int CY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

// https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-blendfunction
[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-windowpos
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS
{
    public nint Hwnd;
    public nint HwndInsertAfter;
    public int X;
    public int Y;
    public int Cx;
    public int Cy;
    public uint Flags;
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-msg
// HookEngine 専用スレッドの手動メッセージポンプ（PeekMessageW/TranslateMessage/DispatchMessageW）用。
[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint Hwnd;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public POINT Pt;
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-kbdllhookstruct
[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint VkCode;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public nint DwExtraInfo;
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-msllhookstruct
[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT Pt;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint DwExtraInfo;
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-guithreadinfo
[StructLayout(LayoutKind.Sequential)]
internal struct GUITHREADINFO
{
    public uint CbSize;
    public uint Flags;
    public nint HwndActive;
    public nint HwndFocus;
    public nint HwndCapture;
    public nint HwndMenuOwner;
    public nint HwndMoveSize;
    public nint HwndCaret;
    public RECT RcCaret;

    public static GUITHREADINFO Create() => new() { CbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
}

// WindowCommands.MaximizeActiveWindow（現行 maximizeActiveWindow()）が最大化中かどうかの判定に使う。
// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-windowplacement
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public uint Length;
    public uint Flags;
    public uint ShowCmd;
    public POINT PtMinPosition;
    public POINT PtMaxPosition;
    public RECT RcNormalPosition;

    public static WINDOWPLACEMENT Create() => new() { Length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-monitorinfoexw
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEXW
{
    public uint CbSize;
    public RECT RcMonitor;
    public RECT RcWork;
    public uint DwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string SzDevice;

    public static MONITORINFOEXW Create() => new() { CbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>(), SzDevice = string.Empty };
}

// https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-devmodew（cmd 18/23 の回転用に必要な範囲のみ）
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEVMODEW
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DmDeviceName;
    public ushort DmSpecVersion;
    public ushort DmDriverVersion;
    public ushort DmSize;
    public ushort DmDriverExtra;
    public uint DmFields;

    public int DmPositionX;
    public int DmPositionY;
    public uint DmDisplayOrientation;
    public uint DmDisplayFixedOutput;

    public short DmColor;
    public short DmDuplex;
    public short DmYResolution;
    public short DmTTOption;
    public short DmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DmFormName;
    public ushort DmLogPixels;
    public uint DmBitsPerPel;
    public uint DmPelsWidth;
    public uint DmPelsHeight;
    public uint DmDisplayFlagsOrNup;
    public uint DmDisplayFrequency;
    public uint DmICMMethod;
    public uint DmICMIntent;
    public uint DmMediaType;
    public uint DmDitherType;
    public uint DmReserved1;
    public uint DmReserved2;
    public uint DmPanningWidth;
    public uint DmPanningHeight;
}

// https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-input
[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT Mi;
    [FieldOffset(0)] public KEYBDINPUT Ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort Vk;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

#endregion

#region delegate

internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
internal delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);
internal delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);
internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);
internal delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

#endregion

#region 定数

internal static class NativeConstants
{
    // ---- ウィンドウスタイル ----
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ---- メッセージ ----
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_SIZE = 0x0005;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_IME_CONTROL = 0x0283;
    public const int SIZE_MINIMIZED = 1;
    public const uint SPI_GETWORKAREA = 0x0030;
    public const int SPI_SETWORKAREA = 0x002F;
    public const nint IMC_GETOPENSTATUS = 0x0005;

    // ---- SetWindowPos ----
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public static readonly nint HWND_BOTTOM = 1;
    public static readonly nint HWND_TOPMOST = -1;
    public static readonly nint HWND_NOTOPMOST = -2;
    // メッセージ専用ウィンドウの親（App.cs の MainWindowHandle 用。可視ウィンドウを作らない）。
    public static readonly nint HWND_MESSAGE = -3;

    // ---- ShowWindow ----
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    // WINDOWPLACEMENT.ShowCmd の比較値としては Win32 の慣例で SW_SHOWMAXIMIZED の名を使う
    // （値は SW_MAXIMIZE と同じ 3）。WindowCommands.MaximizeActiveWindow 用。
    public const int SW_SHOWMAXIMIZED = 3;

    // ---- UpdateLayeredWindow ----
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    // ---- フック ----
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;
    public const int LLKHF_UP = 0x80;

    // ---- HookEngine 専用スレッドのメッセージループ ----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-msgwaitformultipleobjects
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint PM_REMOVE = 0x0001;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_MOUSEHWHEEL = 0x020E;

    // ---- WinEvent（WinEventHook.cpp: フォアグラウンド変化のみを監視。フォーカス変化は
    //     アプリ内コントロール遷移で大量発火しデバウンスを浪費するため使わない） ----
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    // ---- ウィンドウ列挙（HwndUtil: multInst=false のホットキー起動時に既存ウィンドウを探す） ----
    public const uint GW_OWNER = 4;

    // ---- プロセス情報取得（HwndUtil.GetProcessNameByPid） ----
    // https://learn.microsoft.com/windows/win32/procthread/process-security-and-access-rights
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ---- ホットキー ----
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ---- SendMessageTimeout ----
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // ---- クリップボード ----
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;

    // ---- 入力送出 ----
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    // MouseCommands.MoveMouse（絶対座標移動）が正規化座標（0〜65535）であることを示すフラグ。
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    // マウスホイール 1 ノッチ分の標準値。MouseCommands.MouseScroll の mouseData 算出に使用。
    public const uint WHEEL_DELTA = 120;

    // ---- GetSystemMetrics（MouseCommands.MoveMouse: 絶対座標移動時の正規化用スクリーンサイズ取得） ----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getsystemmetrics
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    // ---- 仮想キー（IME 切替・言語切替キー検出、KeyboardHook.cpp と同じ範囲） ----
    public const int VK_HANGEUL = 0x15; // IME On/Off と共用（Kana）
    public const int VK_KANJI = 0x19;
    public const int VK_CONVERT = 0x1C;
    public const int VK_NONCONVERT = 0x1D;
    public const int VK_DBE_ALPHANUMERIC = 0x0F0;
    public const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    public const int VK_MEDIA_STOP = 0xB2;
    public const int VK_MEDIA_NEXT_TRACK = 0xB0;
    public const int VK_MEDIA_PREV_TRACK = 0xB1;
    public const int VK_VOLUME_MUTE = 0xAD;
    public const int VK_VOLUME_UP = 0xAF;
    public const int VK_VOLUME_DOWN = 0xAE;
    public const int VK_SNAPSHOT = 0x2C;
    public const int VK_MENU = 0x12;
    public const int VK_TAB = 0x09;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    // ---- 電源オーバーレイ GUID（PowerModeService.cpp と同値） ----
    // 最適な電力効率
    public static readonly Guid GuidPowerOverlayBestPowerEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    // バランス = GUID_NULL
    public static readonly Guid GuidPowerOverlayBalanced = Guid.Empty;
    // 最適なパフォーマンス
    public static readonly Guid GuidPowerOverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    // ---- DPI ----
    public const int MDT_EFFECTIVE_DPI = 0;

    // ---- プロセスアクセス権（ProcessPriorityService.IsAccessibleForControl 専用） ----
    public const uint PROCESS_SET_INFORMATION = 0x0200;

    // ---- モニター（MonitorFromPoint/MonitorFromWindow の dwFlags、013-ime-corner-image T021） ----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-monitorfrompoint
    public const uint MONITOR_DEFAULTTONULL = 0x00000000;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    // MONITORINFOEXW.DwFlags に立つプライマリモニターフラグ。
    // https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-monitorinfo
    public const uint MONITORINFOF_PRIMARY = 0x00000001;
}

#endregion
