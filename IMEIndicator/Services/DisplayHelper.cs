namespace IMEIndicator.Services;

/// <summary>
/// マルチディスプレイ関連のヘルパーメソッド
/// Win32 API (EnumDisplayMonitors/GetMonitorInfo) を使用してモニター情報を取得
/// </summary>
public static class DisplayHelper
{
    /// <summary>
    /// 全モニターの情報を取得する
    /// </summary>
    private static NativeMethods.MONITORINFOEX[] GetAllMonitors()
    {
        var monitors = new List<NativeMethods.MONITORINFOEX>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = NativeMethods.MONITORINFOEX.Create();
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    monitors.Add(info);
                }
                return true;
            }, IntPtr.Zero);

        return monitors.ToArray();
    }

    /// <summary>
    /// モニター数を取得する
    /// </summary>
    public static int GetScreenCount()
    {
        return GetAllMonitors().Length;
    }

    /// <summary>
    /// 指定モニターのBoundsを取得する
    /// </summary>
    public static (int Left, int Top, int Width, int Height) GetScreenBounds(int index)
    {
        var monitors = GetAllMonitors();
        if (index < 0 || index >= monitors.Length)
            return (0, 0, 0, 0);

        var rc = monitors[index].rcMonitor;
        return (rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    /// <summary>
    /// 座標からディスプレイインデックスを検出
    /// </summary>
    /// <param name="x">X座標（グローバル）</param>
    /// <param name="y">Y座標（グローバル）</param>
    /// <param name="width">ウィンドウ幅</param>
    /// <param name="height">ウィンドウ高さ</param>
    /// <returns>ディスプレイインデックス（見つからない場合は-1）</returns>
    public static int GetDisplayIndexFromPosition(double x, double y, double width, double height)
    {
        var monitors = GetAllMonitors();
        if (monitors.Length == 0) return -1;

        // ウィンドウの中心座標
        double centerX = x + width / 2;
        double centerY = y + height / 2;

        // 中心座標がどのディスプレイに属するか検出
        for (int i = 0; i < monitors.Length; i++)
        {
            var bounds = monitors[i].rcMonitor;
            if (centerX >= bounds.Left && centerX < bounds.Right &&
                centerY >= bounds.Top && centerY < bounds.Bottom)
            {
                return i;
            }
        }

        // 中心が見つからない場合、最も近いディスプレイを探す
        int nearestIndex = 0;
        double minDistance = double.MaxValue;

        for (int i = 0; i < monitors.Length; i++)
        {
            var bounds = monitors[i].rcMonitor;
            double screenCenterX = bounds.Left + (bounds.Right - bounds.Left) / 2.0;
            double screenCenterY = bounds.Top + (bounds.Bottom - bounds.Top) / 2.0;

            double distance = Math.Sqrt(
                Math.Pow(centerX - screenCenterX, 2) +
                Math.Pow(centerY - screenCenterY, 2));

            if (distance < minDistance)
            {
                minDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    /// <summary>
    /// 指定されたディスプレイインデックスが有効かチェック
    /// </summary>
    public static bool IsValidDisplayIndex(int displayIndex)
    {
        var monitors = GetAllMonitors();
        return displayIndex >= 0 && displayIndex < monitors.Length;
    }

    /// <summary>
    /// 座標が有効なディスプレイ内にあるかチェック
    /// </summary>
    public static bool IsPositionOnAnyDisplay(double x, double y)
    {
        var monitors = GetAllMonitors();
        foreach (var monitor in monitors)
        {
            var bounds = monitor.rcMonitor;
            if (x >= bounds.Left && x < bounds.Right &&
                y >= bounds.Top && y < bounds.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// プライマリモニターのワーキングエリア（タスクバーを除く領域）を物理ピクセルで返す
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) GetPrimaryWorkArea()
    {
        var monitors = GetAllMonitors();
        foreach (var monitor in monitors)
        {
            if ((monitor.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0)
            {
                var rc = monitor.rcWork;
                return (rc.Left, rc.Top, rc.Right, rc.Bottom);
            }
        }
        // プライマリが見つからない場合は最初のモニターにフォールバック
        if (monitors.Length > 0)
        {
            var rc = monitors[0].rcWork;
            return (rc.Left, rc.Top, rc.Right, rc.Bottom);
        }
        return (0, 0, 0, 0);
    }

    /// <summary>
    /// 座標がディスプレイ外の場合、フォールバック位置を返す
    /// </summary>
    /// <param name="x">X座標</param>
    /// <param name="y">Y座標</param>
    /// <param name="width">ウィンドウ幅</param>
    /// <param name="height">ウィンドウ高さ</param>
    /// <param name="preferredDisplayIndex">優先ディスプレイインデックス</param>
    /// <param name="useTopRight">右上に配置するか（falseなら左上）</param>
    /// <returns>調整後の座標とディスプレイインデックス</returns>
    public static (double x, double y, int displayIndex) GetValidPosition(
        double x, double y, double width, double height,
        int preferredDisplayIndex, bool useTopRight = false)
    {
        var monitors = GetAllMonitors();
        if (monitors.Length == 0)
        {
            return (x, y, 0);
        }

        // 優先ディスプレイが有効かチェック
        int targetDisplay = preferredDisplayIndex;
        if (targetDisplay < 0 || targetDisplay >= monitors.Length)
        {
            targetDisplay = 0;
        }

        // 現在の座標が有効か確認
        double centerX = x + width / 2;
        double centerY = y + height / 2;
        bool isValid = IsPositionOnAnyDisplay(centerX, centerY);

        if (isValid)
        {
            // 座標は有効だが、ディスプレイインデックスを更新
            int detectedDisplay = GetDisplayIndexFromPosition(x, y, width, height);
            return (x, y, detectedDisplay >= 0 ? detectedDisplay : targetDisplay);
        }

        // 無効な座標の場合、ターゲットディスプレイのデフォルト位置に配置
        var workArea = monitors[targetDisplay].rcWork;
        const double offset = 10;

        double newX, newY;
        if (useTopRight)
        {
            newX = workArea.Right - width - offset;
            newY = workArea.Top + offset;
        }
        else
        {
            newX = workArea.Left + offset;
            newY = workArea.Top + offset;
        }
        return (newX, newY, targetDisplay);
    }
}
