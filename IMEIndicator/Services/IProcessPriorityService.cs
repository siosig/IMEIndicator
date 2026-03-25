using System.Diagnostics;

namespace IMEIndicator.Services;

/// <summary>
/// プロセス優先度操作のインターフェース（テスト用モック対応）
/// </summary>
public interface IProcessPriorityService
{
    /// <summary>
    /// 指定プロセス名の全インスタンスの現在の優先度を取得する。
    /// プロセスが見つからない場合は空リストを返す。
    /// </summary>
    IReadOnlyList<(int ProcessId, ProcessPriorityClass CurrentPriority)> GetProcessPriorities(string processName);

    /// <summary>
    /// 指定プロセスIDの優先度を設定する
    /// </summary>
    /// <returns>成功した場合 true</returns>
    bool SetPriority(int processId, ProcessPriorityClass priority);

    /// <summary>
    /// 指定プロセスIDの現在のプロセッサアフィニティを取得する
    /// </summary>
    /// <returns>アフィニティマスク。取得失敗時は null</returns>
    long? GetAffinity(int processId);

    /// <summary>
    /// 指定プロセスIDのプロセッサアフィニティを設定する
    /// </summary>
    /// <returns>成功した場合 true</returns>
    bool SetAffinity(int processId, long affinityMask);
}
