// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;

namespace IMEIndicator.Interop;

// advapi32.dll。Services/Hotkey/Commands/PowerCommands.cs の SeShutdownPrivilege 取得専用
// （移植元 src/cpp/services/hotkey/commands/PowerCommands.cpp の enableShutdownPrivilege()
// と同じ OpenProcessToken → LookupPrivilegeValueW → AdjustTokenPrivileges の 3 手順）。
// いずれも単純な blittable 構造体のみを扱うため LibraryImport（ソース生成）で解決できる。
internal static partial class NativeMethods
{
    // https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-lookupprivilegevaluew
    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    // https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges
    // 呼び出し元（PowerCommands.cs）は常に PreviousState / ReturnLengthInBytes へ NULL を渡す運用
    // （C++ 版 AdjustTokenPrivileges(hToken.get(), FALSE, &tp, 0, nullptr, nullptr) と同じ）なので、
    // その 2 引数は out 構造体ではなく nint とし、呼び出し側から nint.Zero を渡せるようにする。
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(
        nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLengthInBytes,
        nint previousState,
        nint returnLengthInBytes);
}

// https://learn.microsoft.com/windows/win32/api/ntdef/ns-ntdef-luid
[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

// https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-token_privileges
// 本来 Privileges は LUID_AND_ATTRIBUTES の可変長配列（Privileges[ANYSIZE_ARRAY]）だが、
// PowerCommands.cs は SeShutdownPrivilege 1 件のみを有効化する用途に限定して使うため、
// PrivilegeCount=1 固定で LUID_AND_ATTRIBUTES 1 件分（Luid + Attributes）を
// フィールドとして直接埋め込む（P/Invoke で 1 要素の可変長配列構造体を扱う定番の単純化）。
[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public LUID Luid;
    public uint Attributes;
}
