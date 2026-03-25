using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IMEIndicator.Services;

/// <summary>
/// E-Core/P-Core情報を取得・キャッシュする静的クラス。
/// Intel第12世代以降のハイブリッドアーキテクチャに対応。
/// </summary>
public class ECoreCpuInfo
{
    private static readonly Lazy<ECoreCpuInfo> _instance = new(Create);

    /// <summary>シングルトンインスタンス（起動時に1回だけ取得）</summary>
    public static ECoreCpuInfo Instance => _instance.Value;

    /// <summary>E-Coreが存在するかどうか</summary>
    public bool HasECores { get; }

    /// <summary>E-Coreのみのプロセッサアフィニティビットマスク</summary>
    public long ECoreMask { get; }

    /// <summary>E-Coreの論理プロセッサ数</summary>
    public int ECoreCount { get; }

    /// <summary>P-Coreの論理プロセッサ数</summary>
    public int PCoreCount { get; }

    private ECoreCpuInfo(bool hasECores, long eCoreMask, int eCoreCount, int pCoreCount)
    {
        HasECores = hasECores;
        ECoreMask = eCoreMask;
        ECoreCount = eCoreCount;
        PCoreCount = pCoreCount;
    }

    private static ECoreCpuInfo Create()
    {
        try
        {
            var cores = GetCpuSetInfo();
            if (cores.Count == 0)
                return new ECoreCpuInfo(false, 0, 0, 0);

            // EfficiencyClass が複数種類あればハイブリッドCPU
            var efficiencyClasses = cores.Select(c => c.EfficiencyClass).Distinct().ToList();
            if (efficiencyClasses.Count <= 1)
                return new ECoreCpuInfo(false, 0, 0, 0);

            // EfficiencyClass == 0 がE-Core、>= 1 がP-Core
            byte minEfficiency = efficiencyClasses.Min();
            long eCoreMask = 0;
            int eCoreCount = 0;
            int pCoreCount = 0;

            foreach (var core in cores)
            {
                if (core.EfficiencyClass == minEfficiency)
                {
                    eCoreMask |= 1L << core.LogicalProcessorIndex;
                    eCoreCount++;
                }
                else
                {
                    pCoreCount++;
                }
            }

            bool hasECores = eCoreCount > 0 && pCoreCount > 0;
            return new ECoreCpuInfo(hasECores, hasECores ? eCoreMask : 0, eCoreCount, pCoreCount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ECoreCpuInfo] CPU情報取得に失敗: {ex.Message}");
            return new ECoreCpuInfo(false, 0, 0, 0);
        }
    }

    private static List<CpuSetEntry> GetCpuSetInfo()
    {
        var entries = new List<CpuSetEntry>();

        // バッファサイズ取得
        GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint bufferSize, IntPtr.Zero, 0);
        if (bufferSize == 0)
            return entries;

        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, bufferSize, out _, IntPtr.Zero, 0))
                return entries;

            int offset = 0;
            while (offset < bufferSize)
            {
                var info = Marshal.PtrToStructure<SYSTEM_CPU_SET_INFORMATION>(buffer + offset);

                // Type 0 = CpuSetInformation
                if (info.Type == 0)
                {
                    entries.Add(new CpuSetEntry
                    {
                        LogicalProcessorIndex = info.LogicalProcessorIndex,
                        EfficiencyClass = info.EfficiencyClass
                    });
                }

                offset += (int)info.Size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    private struct CpuSetEntry
    {
        public byte LogicalProcessorIndex;
        public byte EfficiencyClass;
    }

    #region P/Invoke

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(
        IntPtr information,
        uint bufferLength,
        out uint returnedLength,
        IntPtr process,
        uint reserved);

    [StructLayout(LayoutKind.Explicit)]
    private struct SYSTEM_CPU_SET_INFORMATION
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public int Type;
        [FieldOffset(8)] public uint Id;
        [FieldOffset(12)] public ushort Group;
        [FieldOffset(14)] public byte LogicalProcessorIndex;
        [FieldOffset(15)] public byte CoreIndex;
        [FieldOffset(16)] public byte LastLevelCacheIndex;
        [FieldOffset(17)] public byte NumaNodeIndex;
        [FieldOffset(18)] public byte EfficiencyClass;
        [FieldOffset(19)] public byte AllFlags;
        [FieldOffset(20)] public uint SchedulingClass;
        [FieldOffset(24)] public ulong AllocationTag;
    }

    #endregion
}
