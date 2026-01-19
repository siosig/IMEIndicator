namespace IMEIndicatorClock.Services;

/// <summary>
/// 日本語IME検出ロジック
/// </summary>
public static class IMEDetector_Japanese
{
    /// <summary>
    /// 日本語IME状態を処理
    /// </summary>
    /// <param name="hwndForeground">フォアグラウンドウィンドウハンドル</param>
    /// <param name="currentState">現在の状態</param>
    /// <param name="trackedIMEState">トラッキング中のIME状態（参照）</param>
    /// <param name="usePixelStateForJapanese">ピクセル状態使用フラグ（参照）</param>
    /// <param name="useTrackedStateForJapanese">トラッキング状態使用フラグ</param>
    /// <param name="windowChanged">ウィンドウ変更フラグ</param>
    /// <param name="languageChanged">言語変更フラグ</param>
    /// <param name="reliableStatus">信頼できる状態かどうか</param>
    /// <param name="pixelVerificationIntervalMs">ピクセル検証間隔</param>
    /// <param name="lastPixelVerification">最後のピクセル検証時刻（参照）</param>
    /// <param name="debugInfo">デバッグ情報（参照）</param>
    /// <returns>更新後のLanguageInfo</returns>
    public static LanguageInfo ProcessJapaneseIME(
        IntPtr hwndForeground,
        LanguageInfo currentState,
        ref bool trackedIMEState,
        ref bool usePixelStateForJapanese,
        bool useTrackedStateForJapanese,
        bool windowChanged,
        bool languageChanged,
        bool reliableStatus,
        int pixelVerificationIntervalMs,
        ref DateTime lastPixelVerification,
        ref string debugInfo)
    {
        // ウィンドウ切り替え時はピクセル状態優先フラグをリセット
        if (windowChanged || languageChanged)
        {
            usePixelStateForJapanese = false;
        }

        // ピクセル判定タイミングかチェック
        var now = DateTime.Now;
        bool isPixelVerificationTime = pixelVerificationIntervalMs > 0 &&
            (now - lastPixelVerification).TotalMilliseconds >= pixelVerificationIntervalMs;

        if (windowChanged || languageChanged || isPixelVerificationTime)
        {
            var pixelResult = PixelIMEDetector.Instance.DetectIMEState(LanguageType.Japanese);
            if (pixelResult.HasValue)
            {
                lastPixelVerification = now;
                usePixelStateForJapanese = true;

                if (windowChanged || languageChanged)
                {
                    DbgLog.Log(4, $"日本語初期状態(Pixel): {(pixelResult.Value ? "あ" : "A")}");
                }
                else if (trackedIMEState != pixelResult.Value)
                {
                    DbgLog.Log(4, $"日本語状態補正(Pixel): {trackedIMEState} → {pixelResult.Value}");
                }

                trackedIMEState = pixelResult.Value;
                currentState = new LanguageInfo(currentState.Language, pixelResult.Value);
                debugInfo += $" [JapanesePixel:{pixelResult.Value}]";
            }
            else
            {
                usePixelStateForJapanese = false;
            }
        }

        // ピクセル判定成功済みの場合、トラッキング状態を維持
        if (usePixelStateForJapanese)
        {
            currentState = new LanguageInfo(currentState.Language, trackedIMEState);
            if (!debugInfo.Contains("[JapanesePixel"))
            {
                debugInfo += $" [JapanesePixelCached:{trackedIMEState}]";
            }
        }
        else if (useTrackedStateForJapanese)
        {
            // キーフック後はトラッキング状態を使用
            currentState = new LanguageInfo(currentState.Language, trackedIMEState);
            debugInfo += $" [JapaneseTracked:{trackedIMEState}]";
        }
        else if (reliableStatus)
        {
            // API結果が信頼できる場合はそれを使用
            trackedIMEState = currentState.IsIMEOn;
        }

        return currentState;
    }
}
