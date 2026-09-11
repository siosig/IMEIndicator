// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# ポート。
// namespace の変更、戻り値を IMEIndicator.Services.Hotkey.ExecuteError? へ統合)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// 電源管理コマンド（内部コマンド ID 2 / 3 / 4 / 5 / 19 / 38 / 63 / 64。
/// contracts/internal-command-catalog.md「電源（8）」）。
/// 移植元: src/cpp/services/hotkey/commands/PowerCommands.h / .cpp。
/// ExitWindowsEx / SetSuspendState / LockWorkStation を使用し、シャットダウン・再起動のみ
/// 事前に SeShutdownPrivilege を有効化する（C++ 版 enableShutdownPrivilege() と同じ手順）。
///
/// ID→メソッドの振り分けは CommandExecutor.cs（別タスクで実装）が行うため、本クラスは
/// ID を扱わず動作単位のメソッドのみを公開する。C++ 版 CommandExecutor.cpp の「電源コマンド」
/// switch 文では ID 38（表示名は「シャットダウンダイアログ」）も <see cref="Shutdown"/> と同じ
/// shutdownSystem() を呼んでおり、ダイアログを出さず即シャットダウンする（表示名と実装が
/// 乖離している既知のケース。internal-command-catalog.md 65 行目 ⚠ 参照）。C# 版でも同じ関数
/// （<see cref="Shutdown"/>）を割り当てる想定で、是正はしない。
///
/// また ID 19（表示名は「モニター電源オフ」）は CommandExecutor.cpp の電源コマンド switch で
/// 先に処理され <see cref="TurnOffMonitor"/> が呼ばれる。後段のディスプレイコマンド switch にも
/// case 19 があるが、電源コマンド側で既に return 済みのため到達しない（到達不能コード）。
///
/// なお C++ 版 PowerCommands.h/.cpp が持つ startScreenSaver() は CommandExecutor.cpp のどの
/// switch 文からも呼ばれていない（到達不能コード）。ID 6（表示名は「電源: スクリーンセーバー
/// 起動」）は実際には SystemCommands.cpp 側で taskmgr.exe 起動を行っており
/// （internal-command-catalog.md 182 行目 ⚠ 参照）、本クラスの担当 ID（2/3/4/5/19/38/63/64）に
/// 含まれないため、本移植では startScreenSaver 相当のメソッドを設けない。
/// </summary>
public static class PowerCommands
{
    // ---- ExitWindowsEx フラグ・理由コード（winuser.h / winnt.h）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-exitwindowsex
    private const uint EwxLogoff = 0x00000000;
    private const uint EwxShutdown = 0x00000001;
    private const uint EwxReboot = 0x00000002;
    private const uint EwxForce = 0x00000004;

    // SHTDN_REASON_MAJOR_OTHER（winnt.h）。C++ 版と同じ理由コードをそのまま使う。
    private const uint ShtdnReasonMajorOther = 0x00000000;

    // ---- モニター電源制御（winuser.h）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nm-winuser-wm_syscommand
    private const uint WmSyscommand = 0x0112;
    private const nuint ScMonitorpower = 0xF170;
    private const nint HwndBroadcast = 0xffff;
    // SC_MONITORPOWER の lParam: 2 = オフ, 1 = 低電力, -1 = オン（C++ 版 turnOffMonitor() と同じ）。
    private const nint MonitorPowerOff = 2;

    // ---- SeShutdownPrivilege（winnt.h の SE_SHUTDOWN_NAME、TOKEN_* アクセス権）----
    private const string SeShutdownPrivilegeName = "SeShutdownPrivilege";
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    // GetCurrentProcess() の戻り値は常に疑似ハンドル (HANDLE)-1 であり CloseHandle 不要
    // （プロセスの実ハンドルではなく特殊な定数値のため、値が安定していて P/Invoke 宣言を
    // 追加しなくても直接埋め込める）。
    // https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentprocess
    private const nint CurrentProcessPseudoHandle = -1;

    /// <summary>
    /// シャットダウン（<c>shutdownSystem()</c> 相当）。内部コマンド ID 2（シャットダウン）・
    /// 38（シャットダウンダイアログ、実際はダイアログ無しで即シャットダウン）の両方から呼ばれる。
    /// </summary>
    public static ExecuteError? Shutdown()
    {
        if (!TryEnableShutdownPrivilege())
        {
            return ExecuteError.AccessDenied;
        }

        return NativeMethods.ExitWindowsEx(EwxShutdown | EwxForce, ShtdnReasonMajorOther)
            ? null
            : ExecuteError.ApiCallFailed;
    }

    /// <summary>再起動（<c>rebootSystem()</c> 相当）。内部コマンド ID 3。</summary>
    public static ExecuteError? Restart()
    {
        if (!TryEnableShutdownPrivilege())
        {
            return ExecuteError.AccessDenied;
        }

        return NativeMethods.ExitWindowsEx(EwxReboot | EwxForce, ShtdnReasonMajorOther)
            ? null
            : ExecuteError.ApiCallFailed;
    }

    /// <summary>
    /// スリープ（サスペンド、<c>sleepSystem()</c> 相当）。内部コマンド ID 4。
    /// SetSuspendState は SeShutdownPrivilege を必要としないため、Shutdown/Restart と異なり
    /// 事前の特権取得は行わない（C++ 版と同じ）。
    /// </summary>
    public static ExecuteError? Sleep()
    {
        // hibernate=false, forceCritical=false, disableWakeEvent=false（C++ 版と同じ引数）。
        return NativeMethods.SetSuspendState(false, false, false)
            ? null
            : ExecuteError.ApiCallFailed;
    }

    /// <summary>ログオフ（<c>logoffUser()</c> 相当）。内部コマンド ID 5。SeShutdownPrivilege 不要。</summary>
    public static ExecuteError? LogOff()
    {
        return NativeMethods.ExitWindowsEx(EwxLogoff | EwxForce, ShtdnReasonMajorOther)
            ? null
            : ExecuteError.ApiCallFailed;
    }

    /// <summary>
    /// モニター電源オフ（<c>turnOffMonitor()</c> 相当）。内部コマンド ID 19。
    /// HWND_BROADCAST への WM_SYSCOMMAND 送出は個々のトップレベルウィンドウの処理結果を
    /// 集約できないため、C++ 版と同じく戻り値を確認せず常に成功として扱う。
    /// </summary>
    public static ExecuteError? TurnOffMonitor()
    {
        NativeMethods.SendMessageW(HwndBroadcast, WmSyscommand, ScMonitorpower, MonitorPowerOff);
        return null;
    }

    /// <summary>画面ロック（<c>lockWorkstation()</c> 相当）。内部コマンド ID 63。</summary>
    public static ExecuteError? LockWorkstation()
    {
        return NativeMethods.LockWorkStation()
            ? null
            : ExecuteError.ApiCallFailed;
    }

    /// <summary>
    /// 休止状態（ハイバネート、<c>hibernateSystem()</c> 相当）。内部コマンド ID 64。
    /// hibernate=true 以外は <see cref="Sleep"/> と同じ引数（C++ 版と同じ）。
    /// </summary>
    public static ExecuteError? Hibernate()
    {
        return NativeMethods.SetSuspendState(true, false, false)
            ? null
            : ExecuteError.ApiCallFailed;
    }

    /// <summary>
    /// SeShutdownPrivilege を有効化する（C++ 版 enableShutdownPrivilege() と同じ手順）。
    /// OpenProcessToken → LookupPrivilegeValueW → AdjustTokenPrivileges の順に呼び出し、
    /// トークンハンドルは finally で必ず CloseHandle する
    /// （C++ 版の wil::unique_handle による RAII 解放に相当）。
    /// </summary>
    private static bool TryEnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
                CurrentProcessPseudoHandle,
                TokenAdjustPrivileges | TokenQuery,
                out nint tokenHandle))
        {
            return false;
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValueW(null, SeShutdownPrivilegeName, out LUID luid))
            {
                return false;
            }

            var privileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled,
            };

            // AdjustTokenPrivileges は要求した特権を実際には付与できなかった場合でも戻り値 TRUE
            // を返すことがあるため、C++ 版と同じく GetLastError() が ERROR_SUCCESS (0) であることも
            // 合わせて確認する。
            // https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges
            bool adjusted = NativeMethods.AdjustTokenPrivileges(
                tokenHandle, false, ref privileges, 0, nint.Zero, nint.Zero);
            int lastError = Marshal.GetLastWin32Error();
            return adjusted && lastError == 0;
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }
}
