using System.ComponentModel;
using System.Diagnostics;

namespace IMEIndicator.Services;

/// <summary>
/// Process.PriorityClass を使用したプロセス優先度操作の実装
/// </summary>
public class ProcessPriorityService : IProcessPriorityService
{
    public IReadOnlyList<(int ProcessId, ProcessPriorityClass CurrentPriority)> GetProcessPriorities(string processName)
    {
        var result = new List<(int, ProcessPriorityClass)>();
        try
        {
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                using (proc)
                {
                    try
                    {
                        result.Add((proc.Id, proc.PriorityClass));
                    }
                    catch (Win32Exception)
                    {
                        // アクセス拒否（システムプロセス等）はスキップ
                    }
                    catch (InvalidOperationException)
                    {
                        // プロセスが既に終了
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessPriorityService] GetProcessPriorities failed for '{processName}': {ex.Message}");
        }
        return result;
    }

    public bool SetPriority(int processId, ProcessPriorityClass priority)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            proc.PriorityClass = priority;
            return true;
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"[ProcessPriorityService] SetPriority failed (access denied) for PID {processId}: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[ProcessPriorityService] SetPriority failed (process exited) for PID {processId}: {ex.Message}");
            return false;
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine($"[ProcessPriorityService] SetPriority failed (process not found) for PID {processId}: {ex.Message}");
            return false;
        }
    }
}
