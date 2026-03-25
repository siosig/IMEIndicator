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
}
