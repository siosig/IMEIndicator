namespace IMEIndicatorClock.Services;

/// <summary>
/// ウィンドウハンドルとIME状態を管理するクラス
/// タイムスタンプベースの有効期限管理でメモリリークを防止
/// </summary>
public class WindowHandleStateManager
{
    private readonly Dictionary<IntPtr, (bool state, DateTime lastAccess)> _states = new();
    private readonly object _lock = new();
    private const int MaxEntries = 100;
    private const int ExpirationMinutes = 30; // 30分アクセスがないエントリは削除

    /// <summary>
    /// ウィンドウハンドルの状態を設定
    /// </summary>
    public void SetState(IntPtr hwnd, bool state)
    {
        if (hwnd == IntPtr.Zero) return;

        lock (_lock)
        {
            _states[hwnd] = (state, DateTime.Now);
            CleanupIfNeeded();
        }
    }

    /// <summary>
    /// ウィンドウハンドルの状態を取得
    /// </summary>
    public bool TryGetState(IntPtr hwnd, out bool state)
    {
        state = false;
        if (hwnd == IntPtr.Zero) return false;

        lock (_lock)
        {
            if (_states.TryGetValue(hwnd, out var entry))
            {
                // ウィンドウが有効かチェック
                if (NativeMethods.IsWindow(hwnd))
                {
                    // アクセス時刻を更新
                    _states[hwnd] = (entry.state, DateTime.Now);
                    state = entry.state;
                    return true;
                }
                else
                {
                    // 無効なウィンドウは削除
                    _states.Remove(hwnd);
                    DbgLog.Log(6, $"無効なウィンドウを削除: 0x{hwnd:X}");
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 必要に応じてクリーンアップを実行
    /// </summary>
    private void CleanupIfNeeded()
    {
        if (_states.Count <= MaxEntries) return;

        var now = DateTime.Now;
        var expiredKeys = new List<IntPtr>();

        // 期限切れまたは無効なウィンドウを収集
        foreach (var kvp in _states)
        {
            bool isExpired = (now - kvp.Value.lastAccess).TotalMinutes > ExpirationMinutes;
            bool isInvalid = !NativeMethods.IsWindow(kvp.Key);

            if (isExpired || isInvalid)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        // 削除
        foreach (var key in expiredKeys)
        {
            _states.Remove(key);
        }

        // まだ多い場合は古い順に削除
        if (_states.Count > MaxEntries)
        {
            var oldestKeys = _states
                .OrderBy(x => x.Value.lastAccess)
                .Take(_states.Count - MaxEntries)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _states.Remove(key);
            }
        }

        DbgLog.Log(5, $"ウィンドウ状態クリーンアップ: 残り{_states.Count}件");
    }

    /// <summary>
    /// すべてのエントリをクリア
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _states.Clear();
        }
    }

    /// <summary>
    /// 現在のエントリ数
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _states.Count;
            }
        }
    }
}
