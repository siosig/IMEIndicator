namespace IMEIndicatorClock.Services;

/// <summary>
/// マルチディスプレイ関連のヘルパーメソッド
/// </summary>
public static class DisplayHelper
{
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
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return -1;

        // ウィンドウの中心座標
        double centerX = x + width / 2;
        double centerY = y + height / 2;

        // 中心座標がどのディスプレイに属するか検出
        for (int i = 0; i < screens.Length; i++)
        {
            var bounds = screens[i].Bounds;
            if (centerX >= bounds.Left && centerX < bounds.Right &&
                centerY >= bounds.Top && centerY < bounds.Bottom)
            {
                return i;
            }
        }

        // 中心が見つからない場合、最も近いディスプレイを探す
        int nearestIndex = 0;
        double minDistance = double.MaxValue;

        for (int i = 0; i < screens.Length; i++)
        {
            var bounds = screens[i].Bounds;
            double screenCenterX = bounds.Left + bounds.Width / 2.0;
            double screenCenterY = bounds.Top + bounds.Height / 2.0;

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
        var screens = System.Windows.Forms.Screen.AllScreens;
        return displayIndex >= 0 && displayIndex < screens.Length;
    }

    /// <summary>
    /// 座標が有効なディスプレイ内にあるかチェック
    /// </summary>
    public static bool IsPositionOnAnyDisplay(double x, double y)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        foreach (var screen in screens)
        {
            var bounds = screen.Bounds;
            if (x >= bounds.Left && x < bounds.Right &&
                y >= bounds.Top && y < bounds.Bottom)
            {
                return true;
            }
        }
        return false;
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
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return (x, y, 0);
        }

        // 優先ディスプレイが有効かチェック
        int targetDisplay = preferredDisplayIndex;
        if (!IsValidDisplayIndex(targetDisplay))
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
        var screen = screens[targetDisplay].WorkingArea;
        const double offset = 10;

        double newX, newY;
        if (useTopRight)
        {
            newX = screen.Right - width - offset;
            newY = screen.Top + offset;
        }
        else
        {
            newX = screen.Left + offset;
            newY = screen.Top + offset;
        }

        DbgLog.Log(3, $"DisplayHelper: 無効な座標 ({x},{y}) → フォールバック ({newX},{newY}) display={targetDisplay}");
        return (newX, newY, targetDisplay);
    }
}
