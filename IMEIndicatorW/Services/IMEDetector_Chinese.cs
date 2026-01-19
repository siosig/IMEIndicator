namespace IMEIndicatorClock.Services;

/// <summary>
/// 中国語IME検出ロジック（繁体字/簡体字）
/// </summary>
public static class IMEDetector_Chinese
{
    /// <summary>
    /// 中国語IME状態を処理
    /// </summary>
    /// <param name="hwndForeground">フォアグラウンドウィンドウハンドル</param>
    /// <param name="currentState">現在の状態</param>
    /// <param name="trackedIMEState">トラッキング中のIME状態（参照）</param>
    /// <param name="usePixelStateForChinese">ピクセル状態使用フラグ（参照）</param>
    /// <param name="useTrackedStateForChinese">トラッキング状態使用フラグ（参照）</param>
    /// <param name="windowChanged">ウィンドウ変更フラグ</param>
    /// <param name="languageChanged">言語変更フラグ</param>
    /// <param name="pixelVerificationIntervalMs">ピクセル検証間隔</param>
    /// <param name="lastPixelVerification">最後のピクセル検証時刻（参照）</param>
    /// <param name="debugInfo">デバッグ情報（参照）</param>
    /// <returns>更新後のLanguageInfo</returns>
    public static LanguageInfo ProcessChineseIME(
        IntPtr hwndForeground,
        LanguageInfo currentState,
        ref bool trackedIMEState,
        ref bool usePixelStateForChinese,
        ref bool useTrackedStateForChinese,
        bool windowChanged,
        bool languageChanged,
        int pixelVerificationIntervalMs,
        ref DateTime lastPixelVerification,
        ref string debugInfo)
    {
        // ウィンドウ切り替え時はピクセル状態優先フラグをリセット
        if (windowChanged || languageChanged)
        {
            usePixelStateForChinese = false;
        }

        // ピクセル判定タイミングかチェック
        var now = DateTime.Now;
        bool isPixelVerificationTime = pixelVerificationIntervalMs > 0 &&
            (now - lastPixelVerification).TotalMilliseconds >= pixelVerificationIntervalMs;

        if (windowChanged || languageChanged || isPixelVerificationTime)
        {
            var pixelResult = PixelIMEDetector.Instance.DetectIMEState(currentState.Language);
            if (pixelResult.HasValue)
            {
                lastPixelVerification = now;
                usePixelStateForChinese = true;

                if (windowChanged || languageChanged)
                {
                    DbgLog.Log(4, $"中国語初期状態(Pixel): {(pixelResult.Value ? "中" : "英")}");
                }
                else if (trackedIMEState != pixelResult.Value)
                {
                    DbgLog.Log(4, $"中国語状態補正(Pixel): {trackedIMEState} → {pixelResult.Value}");
                }

                trackedIMEState = pixelResult.Value;
                useTrackedStateForChinese = false;
                currentState = new LanguageInfo(currentState.Language, pixelResult.Value);
                debugInfo += $" [ChinesePixel:{pixelResult.Value}]";
            }
            else
            {
                usePixelStateForChinese = false;
            }
        }

        // ピクセル判定成功済みの場合、トラッキング状態を維持
        if (usePixelStateForChinese)
        {
            currentState = new LanguageInfo(currentState.Language, trackedIMEState);
            if (!debugInfo.Contains("[ChinesePixel"))
            {
                debugInfo += $" [ChinesePixelCached:{trackedIMEState}]";
            }
        }
        else if (useTrackedStateForChinese)
        {
            // キーフック後はトラッキング状態を使用
            currentState = new LanguageInfo(currentState.Language, trackedIMEState);
            debugInfo += $" [ChineseTracked:{trackedIMEState}]";
        }

        return currentState;
    }
}
