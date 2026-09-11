// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using IMEIndicator.Interop;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// <see cref="IProcessPriorityService"/> の実装。移植元: src/cpp/services/ProcessPriorityService.h / .cpp。
/// </summary>
/// <remarks>
/// research.md R-8 の決定どおり、現行 C++ 版の CreateToolhelp32Snapshot / OpenProcess /
/// GetPriorityClass / SetPriorityClass / GetProcessAffinityMask / SetProcessAffinityMask による
/// 手動列挙・資源管理を、管理 API（<see cref="System.Diagnostics.Process"/>）へ置き換える。
/// <see cref="Process.GetProcessesByName(string)"/> は拡張子なし・大小無視での名前一致列挙を
/// ビルトインで行うため、C++ 版の手動フィルタリング（stripExeExtension + equalsIgnoreCase）は不要。
/// ただし <see cref="IsAccessibleForControl(string)"/> だけは「書き込み権限を実際に試す」という
/// C++ 版の判定（OpenProcess(PROCESS_SET_INFORMATION, ...)）と同じ結果が必要で、.NET の
/// Process クラスには対応する直接 API が無いため、そこだけ P/Invoke を使う。
/// </remarks>
public sealed class ProcessPriorityService : IProcessPriorityService
{
    private int _accessDeniedCount;

    /// <summary>管理者権限不足での失敗回数（累積）。0 でない場合は管理者権限が必要な可能性がある。</summary>
    public int AccessDeniedCount => _accessDeniedCount;

    public IReadOnlyList<ProcessPriorityEntry> GetProcessPriorities(string processNameNoExt)
    {
        var result = new List<ProcessPriorityEntry>();
        foreach (Process process in Process.GetProcessesByName(processNameNoExt))
        {
            using (process)
            {
                try
                {
                    result.Add(new ProcessPriorityEntry(process.Id, process.PriorityClass));
                }
                catch (Win32Exception ex) when (IsAccessDenied(ex))
                {
                    Interlocked.Increment(ref _accessDeniedCount);
                }
                catch (InvalidOperationException)
                {
                    // 列挙後にプロセスが終了した等（移植元の GetPriorityClass 失敗 → スキップに相当）。
                }
            }
        }
        return result;
    }

    public bool SetPriority(int processId, ProcessPriorityClass priorityClass)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.PriorityClass = priorityClass;
            return true;
        }
        catch (Win32Exception ex) when (IsAccessDenied(ex))
        {
            Interlocked.Increment(ref _accessDeniedCount);
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public nint? GetAffinity(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessorAffinity;
        }
        catch (Win32Exception ex) when (IsAccessDenied(ex))
        {
            Interlocked.Increment(ref _accessDeniedCount);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    public bool SetAffinity(int processId, nint affinityMask)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.ProcessorAffinity = affinityMask;
            return true;
        }
        catch (Win32Exception ex) when (IsAccessDenied(ex))
        {
            Interlocked.Increment(ref _accessDeniedCount);
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public bool IsAccessibleForControl(string processNameNoExt)
    {
        Process[] processes = Process.GetProcessesByName(processNameNoExt);
        try
        {
            if (processes.Length == 0)
            {
                return true; // 一致プロセスが 0 件 → 判定不能（警告色を出さない）
            }

            foreach (Process process in processes)
            {
                nint handle = NativeMethods.OpenProcess(NativeConstants.PROCESS_SET_INFORMATION, false, (uint)process.Id);
                if (handle != 0)
                {
                    NativeMethods.CloseHandle(handle);
                    return true; // 1 つでも制御可能なら以降の探索は不要
                }

                // OpenProcess 失敗時、ACCESS_DENIED 以外は判定不能として保守的に true とする（移植元と同じ）。
                if (Marshal.GetLastWin32Error() != 5)
                {
                    return true;
                }
            }

            return false; // 全て ACCESS_DENIED
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// 現在実行中のプロセス名を重複排除・昇順ソート（大小無視）して返す（.exe 拡張子付き）。
    /// 権限不足で取得できないプロセスは無視する。移植元: enumerateDistinctProcessNames()。
    /// </summary>
    public static IReadOnlyList<string> EnumerateDistinctProcessNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    names.Add(process.ProcessName + ".exe");
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // 権限不足・列挙後の終了は無視する。
                }
            }
        }
        return [.. names];
    }

    private static bool IsAccessDenied(Win32Exception ex) => ex.NativeErrorCode == 5; // ERROR_ACCESS_DENIED
}
