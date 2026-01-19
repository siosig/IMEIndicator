//
// IMEMonitor.Debug.cs
// 開発用デバッグ機能（公開リポジトリには含めない）
//
// .gitignore に *.Debug.cs を追加して非公開にする
//

using System.Diagnostics;
using System.Text;

namespace IMEIndicatorClock.Services;

/// <summary>
/// IMEMonitor デバッグ拡張
/// </summary>
public partial class IMEMonitor
{
#if DEBUG
    /// <summary>
    /// デバッグ用: 現在の内部状態をダンプ
    /// </summary>
    public string DumpInternalState()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== IMEMonitor Internal State ===");
        sb.AppendLine($"LastState: {_lastState.Language}, IME={_lastState.IsIMEOn}");
        sb.AppendLine($"TrackedIMEState: {_trackedIMEState}");
        sb.AppendLine($"LastForegroundWindow: 0x{_lastForegroundWindow:X}");
        sb.AppendLine($"UseTrackedStateForJapanese: {_useTrackedStateForJapanese}");
        sb.AppendLine($"UseTrackedStateForChinese: {_useTrackedStateForChinese}");
        sb.AppendLine($"UsePixelStateForKorean: {_usePixelStateForKorean}");
        sb.AppendLine($"UsePixelStateForChinese: {_usePixelStateForChinese}");
        sb.AppendLine($"UsePixelStateForJapanese: {_usePixelStateForJapanese}");
        sb.AppendLine($"WindowKoreanIMEStates Count: {_windowKoreanIMEStates.Count}");
        sb.AppendLine($"PixelVerificationInterval: {_pixelVerificationIntervalMs}ms");
        sb.AppendLine($"LastPixelVerification: {_lastPixelVerification:HH:mm:ss.fff}");
        return sb.ToString();
    }

    /// <summary>
    /// デバッグ用: IME状態を強制的に設定
    /// </summary>
    public void DebugForceIMEState(LanguageType language, bool isIMEOn)
    {
        DbgLog.W($"[DEBUG] IME状態を強制設定: {language}, IME={isIMEOn}");
        _lastState = new LanguageInfo(language, isIMEOn);
        _trackedIMEState = isIMEOn;
        IMEStateChanged?.Invoke(_lastState);
    }

    /// <summary>
    /// デバッグ用: トラッキング状態をリセット
    /// </summary>
    public void DebugResetTracking()
    {
        DbgLog.W("[DEBUG] トラッキング状態をリセット");
        _trackedIMEState = false;
        _useTrackedStateForJapanese = false;
        _useTrackedStateForChinese = false;
        _usePixelStateForKorean = false;
        _usePixelStateForChinese = false;
        _usePixelStateForJapanese = false;
        _windowKoreanIMEStates.Clear();
    }

    /// <summary>
    /// デバッグ用: ウィンドウ情報を詳細出力
    /// </summary>
    public void DebugDumpWindowInfo()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            DbgLog.Log(1, "[DEBUG] フォアグラウンドウィンドウなし");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Window Info ===");
        sb.AppendLine($"HWND: 0x{hwnd:X}");
        sb.AppendLine($"Title: {NativeMethods.GetWindowTitle(hwnd)}");
        sb.AppendLine($"Class: {NativeMethods.GetWindowClassName(hwnd)}");
        sb.AppendLine($"Process: {NativeMethods.GetProcessName(hwnd)}");

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        sb.AppendLine($"ThreadId: {threadId}, ProcessId: {processId}");

        IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
        int langId = (int)hkl & 0xFFFF;
        sb.AppendLine($"KeyboardLayout: 0x{langId:X4} ({IMEDetector_Common.GetLanguageType(langId)})");

        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
        sb.AppendLine($"IMEWnd: {(imeWnd != IntPtr.Zero ? $"0x{imeWnd:X}" : "なし")}");

        IntPtr hIMC = NativeMethods.ImmGetContext(hwnd);
        sb.AppendLine($"IMC: {(hIMC != IntPtr.Zero ? $"0x{hIMC:X}" : "なし")}");
        if (hIMC != IntPtr.Zero)
        {
            bool isOpen = NativeMethods.ImmGetOpenStatus(hIMC);
            sb.AppendLine($"IME Open: {isOpen}");
            NativeMethods.ImmReleaseContext(hwnd, hIMC);
        }

        DbgLog.Log(1, sb.ToString());
    }

    /// <summary>
    /// デバッグ用: ピクセル判定を強制実行
    /// </summary>
    public bool? DebugForcePixelDetection(LanguageType language)
    {
        DbgLog.Log(1, $"[DEBUG] ピクセル判定強制実行: {language}");
        PixelIMEDetector.Instance.ClearCache();
        return PixelIMEDetector.Instance.DetectIMEState(language);
    }
#endif

    /// <summary>
    /// 詳細ログを出力（条件付きコンパイル）
    /// </summary>
    [Conditional("DEBUG")]
    private void LogVerbose(string message)
    {
        DbgLog.Log(6, message);
    }

    /// <summary>
    /// 状態変更をトレース（条件付きコンパイル）
    /// </summary>
    [Conditional("DEBUG")]
    private void TraceStateChange(string context, LanguageInfo oldState, LanguageInfo newState)
    {
        if (oldState != newState)
        {
            DbgLog.Log(3, $"[TRACE] {context}: {oldState.Language}/{oldState.IsIMEOn} → {newState.Language}/{newState.IsIMEOn}");
        }
    }
}
