//
// PixelIMEDetector.Debug.cs
// 開発用デバッグ機能（公開リポジトリには含めない）
//

using System.Diagnostics;
using System.Text;

namespace IMEIndicatorClock.Services;

/// <summary>
/// PixelIMEDetector デバッグ拡張
/// </summary>
public partial class PixelIMEDetector
{
#if DEBUG
    /// <summary>
    /// デバッグ用: キャッシュ状態をダンプ
    /// </summary>
    public string DebugDumpCacheState()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PixelIMEDetector Cache State ===");
        sb.AppendLine($"CachedIndicatorRect: {_cachedIndicatorRect}");
        sb.AppendLine($"LastIndicatorSearch: {_lastIndicatorSearch:HH:mm:ss.fff}");
        sb.AppendLine($"LastPixelCheck: {_lastPixelCheck:HH:mm:ss.fff}");
        sb.AppendLine($"LastPixelResult: {_lastPixelResult?.ToString() ?? "null"}");
        sb.AppendLine($"Disposed: {_disposed}");
        return sb.ToString();
    }

    /// <summary>
    /// デバッグ用: インジケーター位置を強制再検索
    /// </summary>
    public System.Windows.Rect DebugRefreshIndicatorRect()
    {
        DbgLog.Log(1, "[DEBUG] インジケーター位置を強制再検索");
        _cachedIndicatorRect = System.Windows.Rect.Empty;
        _lastIndicatorSearch = DateTime.MinValue;
        return GetIndicatorRect();
    }

    /// <summary>
    /// デバッグ用: 全言語でピクセル判定テスト
    /// </summary>
    public void DebugTestAllLanguages()
    {
        var languages = new[]
        {
            LanguageType.Japanese,
            LanguageType.Korean,
            LanguageType.ChineseSimplified,
            LanguageType.ChineseTraditional
        };

        var sb = new StringBuilder();
        sb.AppendLine("=== Pixel Detection Test ===");

        foreach (var lang in languages)
        {
            ClearCache();
            var result = DetectIMEState(lang);
            sb.AppendLine($"{lang}: {result?.ToString() ?? "null"}");
        }

        DbgLog.Log(1, sb.ToString());
    }

    /// <summary>
    /// デバッグ用: ピクセルデータを詳細分析
    /// </summary>
    public void DebugAnalyzeCurrentIndicator()
    {
        var rect = GetIndicatorRect();
        if (rect.IsEmpty)
        {
            DbgLog.Log(1, "[DEBUG] インジケーターが見つかりません");
            return;
        }

        DbgLog.Log(1, $"[DEBUG] インジケーター位置: {rect}");
        DbgLog.Log(1, $"[DEBUG] サイズ: {rect.Width}x{rect.Height}");
    }
#endif

    /// <summary>
    /// 詳細ログを出力（条件付きコンパイル）
    /// </summary>
    [Conditional("DEBUG")]
    private void LogPixelDebug(string message)
    {
        DbgLog.Log(6, $"[Pixel] {message}");
    }
}
