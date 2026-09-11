// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services;

/// <summary>
/// E-Core / P-Core 情報をプロセス起動時に 1 回取得してキャッシュするシングルトン。
/// 移植元: src/cpp/services/ECoreCpuInfo.h / .cpp の class ECoreCpuInfo。
/// Intel 第 12 世代以降のハイブリッドアーキテクチャ対応。
/// </summary>
/// <remarks>
/// GetSystemCpuSetInformation（kernel32.dll、Windows 10 以降のみ export）は
/// NativeMethods.Kernel32.cs のコメントの通り GetProcAddress で動的解決する
/// （静的な LibraryImport/DllImport で束縛すると、旧環境で型ロード時に
/// DllNotFoundException/EntryPointNotFoundException が評価される可能性があるため、
/// 実行時解決でこのクラス単体の失敗に閉じ込める）。取得したバッファを
/// SYSTEM_CPU_SET_INFORMATION（<see cref="SystemCpuSetInfo"/>、C++ 版と同一レイアウト）の
/// 可変長配列として解釈し、EfficiencyClass の最小値に該当する論理プロセッサを E-Core と判定する。
/// </remarks>
public sealed class ECoreCpuInfo
{
    // CPU_SET_INFORMATION_TYPE::CpuSetInformation（winnt.h）。C++ 版の Type == 0 判定と同じ。
    private const int CpuSetInformationType = 0;

    private static readonly Lazy<ECoreCpuInfo> LazyInstance = new(() => new ECoreCpuInfo());

    /// <summary>プロセス内で共有するシングルトンインスタンス（初回アクセス時に 1 回だけ検出する）。</summary>
    public static ECoreCpuInfo Instance => LazyInstance.Value;

    private ECoreCpuInfo()
    {
        Detect();
    }

    /// <summary>ハイブリッドアーキテクチャ（E-Core を持つ）かどうか。</summary>
    public bool HasECores { get; private set; }

    /// <summary>
    /// E-Core に該当する論理プロセッサのアフィニティマスク（<see cref="HasECores"/> が false の場合 0）。
    /// <see cref="System.Diagnostics.Process.ProcessorAffinity"/>（型は IntPtr = nint）へそのまま
    /// 代入できるよう nint 型にしている。
    /// </summary>
    public nint ECoreMask { get; private set; }

    /// <summary>E-Core の論理プロセッサ数。</summary>
    public int ECoreCount { get; private set; }

    /// <summary>P-Core（E-Core 以外）の論理プロセッサ数。</summary>
    public int PCoreCount { get; private set; }

    private void Detect()
    {
        nint kernel32 = NativeMethods.GetModuleHandleW("kernel32.dll");
        if (kernel32 == 0)
        {
            return;
        }

        nint proc = NativeMethods.GetProcAddress(kernel32, "GetSystemCpuSetInformation");
        if (proc == 0)
        {
            return;
        }

        var getSystemCpuSetInformation =
            Marshal.GetDelegateForFunctionPointer<GetSystemCpuSetInformationDelegate>(proc);

        // 1 回目: 必要なバッファサイズだけを問い合わせる（戻り値は FALSE が期待されるため無視）。
        getSystemCpuSetInformation(0, 0, out uint bufferSize, 0, 0);
        if (bufferSize == 0)
        {
            return;
        }

        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (!getSystemCpuSetInformation(buffer, bufferSize, out bufferSize, 0, 0))
            {
                return;
            }

            var entries = new List<SystemCpuSetInfo>();
            uint offset = 0;
            while (offset + sizeof(uint) <= bufferSize)
            {
                SystemCpuSetInfo info = Marshal.PtrToStructure<SystemCpuSetInfo>(IntPtr.Add(buffer, (int)offset));
                if (info.Size == 0 || offset + info.Size > bufferSize)
                {
                    break;
                }

                if (info.Type == CpuSetInformationType)
                {
                    entries.Add(info);
                }

                offset += info.Size;
            }

            if (entries.Count == 0)
            {
                return;
            }

            // EfficiencyClass の distinct 集合が 1 種類のみなら非ハイブリッド。
            // 「entries[0] と異なるクラスが 1 つでもあれば distinct 数 >= 2」という等価な条件を
            // 1 パスで判定する（std::set を使う C++ 版と同じ結果になる）。
            byte firstClass = entries[0].EfficiencyClass;
            byte minClass = firstClass;
            bool hasMultipleClasses = false;
            foreach (SystemCpuSetInfo entry in entries)
            {
                if (entry.EfficiencyClass != firstClass)
                {
                    hasMultipleClasses = true;
                }

                if (entry.EfficiencyClass < minClass)
                {
                    minClass = entry.EfficiencyClass;
                }
            }

            if (!hasMultipleClasses)
            {
                return; // 単一クラス → ハイブリッドではない
            }

            nint mask = 0;
            int eCoreCount = 0;
            int pCoreCount = 0;
            foreach (SystemCpuSetInfo entry in entries)
            {
                if (entry.EfficiencyClass == minClass)
                {
                    mask |= (nint)1 << entry.LogicalProcessorIndex;
                    eCoreCount++;
                }
                else
                {
                    pCoreCount++;
                }
            }

            if (eCoreCount > 0 && pCoreCount > 0)
            {
                HasECores = true;
                ECoreMask = mask;
                ECoreCount = eCoreCount;
                PCoreCount = pCoreCount;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Windows SDK winnt.h の SYSTEM_CPU_SET_INFORMATION（CpuSet 共用体メンバー）と同一レイアウト。
    // C++ 版（ECoreCpuInfo.cpp）の #pragma pack(push, 1) と同じく Pack = 1 を指定する。
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SystemCpuSetInfo
    {
        public uint Size;
        public int Type;
        public uint Id;
        public ushort Group;
        public byte LogicalProcessorIndex;
        public byte CoreIndex;
        public byte LastLevelCacheIndex;
        public byte NumaNodeIndex;
        public byte EfficiencyClass;
        public byte AllFlags;
        public uint SchedulingClass;
        public ulong AllocationTag;
    }

    // BOOL GetSystemCpuSetInformation(PSYSTEM_CPU_SET_INFORMATION, ULONG, PULONG, HANDLE, ULONG)
    // https://learn.microsoft.com/windows/win32/api/systemtopologyapi/nf-systemtopologyapi-getsystemcpusetinformation
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetSystemCpuSetInformationDelegate(
        nint information, uint bufferLength, out uint returnedLength, nint process, uint flags);
}
