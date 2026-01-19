namespace IMEIndicatorClock.Services;

/// <summary>
/// IMEMonitor - 韓国語IME処理
/// </summary>
public partial class IMEMonitor
{
    /// <summary>
    /// 韓国語IME状態を処理
    /// </summary>
    private LanguageInfo ProcessKoreanIME(
        IntPtr hwndForeground,
        LanguageInfo currentState,
        bool windowChanged,
        bool languageChanged,
        ref string debugInfo)
    {
        if (windowChanged || languageChanged)
        {
            _usePixelStateForKorean = false;
        }

        var now = DateTime.Now;
        bool isPixelVerificationTime = _pixelVerificationIntervalMs > 0 &&
            (now - _lastPixelVerification).TotalMilliseconds >= _pixelVerificationIntervalMs;

        if (windowChanged || languageChanged || isPixelVerificationTime)
        {
            var pixelResult = PixelIMEDetector.Instance.DetectIMEState(LanguageType.Korean);
            if (pixelResult.HasValue)
            {
                _lastPixelVerification = now;
                _usePixelStateForKorean = true;

                if (windowChanged || languageChanged)
                {
                    DbgLog.Log(4, $"韓国語初期状態(Pixel): {(pixelResult.Value ? "한글" : "영문")}");
                }
                else if (_trackedIMEState != pixelResult.Value)
                {
                    DbgLog.Log(4, $"韓国語状態補正(Pixel): {_trackedIMEState} → {pixelResult.Value}");
                }

                _trackedIMEState = pixelResult.Value;
                currentState = new LanguageInfo(currentState.Language, pixelResult.Value);
                debugInfo += $" [KoreanPixel:{pixelResult.Value}]";
            }
            else
            {
                _usePixelStateForKorean = false;
            }
        }

        if (_usePixelStateForKorean)
        {
            currentState = new LanguageInfo(currentState.Language, _trackedIMEState);
            if (!debugInfo.Contains("[KoreanPixel"))
            {
                debugInfo += $" [KoreanPixelCached:{_trackedIMEState}]";
            }
        }
        else
        {
            var (isHangul, apiSuccess) = IMEDetector_Korean.GetKoreanIMEModeEx(hwndForeground);

            if (apiSuccess)
            {
                _trackedIMEState = isHangul;
                currentState = new LanguageInfo(currentState.Language, isHangul);
                debugInfo += $" [KoreanAPI:{isHangul}]";
            }
            else
            {
                if (windowChanged || languageChanged)
                {
                    if (_lastForegroundWindow != IntPtr.Zero && _lastState.Language == LanguageType.Korean)
                    {
                        _windowKoreanIMEStates.SetState(_lastForegroundWindow, _trackedIMEState);
                        DbgLog.Log(5, $"韓国語状態保存: hwnd=0x{_lastForegroundWindow:X} state={_trackedIMEState}");
                    }

                    if (_windowKoreanIMEStates.TryGetState(hwndForeground, out bool savedState))
                    {
                        _trackedIMEState = savedState;
                        DbgLog.Log(5, $"韓国語状態復元: hwnd=0x{hwndForeground:X} state={savedState}");
                    }
                    else
                    {
                        _trackedIMEState = false;
                        DbgLog.Log(5, $"韓国語初期化: 영문で初期化 (hwnd=0x{hwndForeground:X})");
                    }
                }
                currentState = new LanguageInfo(currentState.Language, _trackedIMEState);
                debugInfo += $" [KoreanTracked:{_trackedIMEState}]";
            }
        }

        return currentState;
    }
}
