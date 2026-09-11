// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;

namespace IMEIndicator.Interop;

// imm32.dll。IME 状態検出（IMEDetector）で使用。現行 C++ 版 IMEDetector.cpp と同じ関数群。
internal static partial class NativeMethods
{
    [LibraryImport("imm32.dll")]
    public static partial nint ImmGetDefaultIMEWnd(nint hWnd);

    [LibraryImport("imm32.dll")]
    public static partial nint ImmGetContext(nint hWnd);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmGetOpenStatus(nint hIMC);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmReleaseContext(nint hWnd, nint hIMC);
}
