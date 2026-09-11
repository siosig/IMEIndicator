// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;

namespace IMEIndicator.Interop;

// powrprof.dll。
// PowerSetActiveOverlayScheme / PowerGetActualOverlayScheme は Win32 API リファレンスに
// 掲載されていない undocumented API だが、DLL のエクスポートテーブルには存在する。
// C# の [LibraryImport] は P/Invoke マーシャラが実行時に LoadLibrary/GetProcAddress で解決するため、
// C++ 版のような静的リンク用 import ライブラリの欠如という制約を受けない（PowerModeService.cpp のコメント参照）。
// 参考: https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme
//      （PowerSetActiveScheme は文書化されているが Overlay 版は非公開 API）
internal static partial class NativeMethods
{
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActualOverlayScheme(out Guid actualOverlayGuid);

    // https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-setsuspendstate
    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetSuspendState([MarshalAs(UnmanagedType.Bool)] bool hibernate, [MarshalAs(UnmanagedType.Bool)] bool forceCritical, [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
}
