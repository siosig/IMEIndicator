// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;

namespace IMEIndicator.Interop;

// winmm.dll。MediaCommands の CD トレイ開閉（MCI）で使用。
internal static partial class NativeMethods
{
    [LibraryImport("winmm.dll", EntryPoint = "mciSendStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int mciSendStringW(string lpszCommand, nint lpszReturnString, uint cchReturn, nint hwndCallback);
}
