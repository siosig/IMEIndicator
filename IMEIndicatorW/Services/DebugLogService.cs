//
//  DebugLogService.cs
//  IMEIndicatorW
//
//  デバッグログ出力ユーティリティ
//  レベル制御とファイル出力機能を提供
//
//  Swift版 dbgLog.swift からの移植
//

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace IMEIndicatorClock.Services;

/// <summary>
/// パス表示スタイル
/// </summary>
public enum PathStyle
{
    FileOnly,      // ファイル名のみ
    Parent1,       // 親ディレクトリ付き
    Parent2,       // 親親ディレクトリ付き
    FullPath       // フルパス
}

/// <summary>
/// デバッグログサービス
/// レベル制御とファイル出力機能を提供
/// </summary>
public static class DebugLogService
{
    // MARK: - 設定

    /// <summary>
    /// デバッグレベル設定
    /// - 0: 何も出力しない
    /// - 1〜99: 指定レベル以下を表示（コンソールのみ）
    /// - -1〜-99: 指定レベル（絶対値）以下をファイルに出力 + コンソールにも出力
    /// </summary>
    private static int _debugLevel = 1;

    /// <summary>
    /// パス表示スタイル設定
    /// </summary>
    private static PathStyle _pathStyle = PathStyle.Parent1;

    /// <summary>
    /// ログファイルパスのキャッシュ
    /// </summary>
    private static string? _cachedLogFilePath;

    /// <summary>
    /// ファイル書き込み用のロックオブジェクト
    /// </summary>
    private static readonly object _fileLock = new();

    /// <summary>
    /// ログファイルの最大サイズ（バイト）- 10MB
    /// </summary>
    private const long MaxLogFileSize = 10 * 1024 * 1024;

    /// <summary>
    /// 保持するバックアップファイル数
    /// </summary>
    private const int MaxBackupFiles = 3;

    // MARK: - 公開プロパティ

    /// <summary>
    /// デバッグレベルを取得・設定
    /// </summary>
    public static int DebugLevel
    {
        get => _debugLevel;
        set => _debugLevel = value;
    }

    /// <summary>
    /// パス表示スタイルを取得・設定
    /// </summary>
    public static PathStyle PathStyle
    {
        get => _pathStyle;
        set => _pathStyle = value;
    }

    /// <summary>
    /// ファイル出力が有効かどうか
    /// </summary>
    public static bool IsFileOutputEnabled => _debugLevel < 0;

    /// <summary>
    /// ログファイルのパスを取得
    /// </summary>
    public static string LogFilePath => GetLogFilePath();

    // MARK: - ログ出力関数

    /// <summary>
    /// デバッグログを出力する
    /// </summary>
    /// <param name="level">ログレベル（1〜99、負の値でファイル出力も）</param>
    /// <param name="message">メッセージ</param>
    /// <param name="filePath">呼び出し元ファイル（自動取得）</param>
    /// <param name="lineNumber">呼び出し元行番号（自動取得）</param>
    /// <param name="memberName">呼び出し元メンバー名（自動取得）</param>
    public static void Log(
        int level,
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        // レベル0の場合は何もしない
        if (_debugLevel == 0) return;

        // レベルチェック（絶対値で比較）
        int threshold = Math.Abs(_debugLevel);
        if (Math.Abs(level) > threshold) return;

        // ファイル出力が必要か判定（levelが負ならファイル出力）
        bool shouldWriteToFile = level < 0;

        // ファイルパスを整形
        string formattedPath = FormatFilePath(filePath);

        // ログメッセージを構築
        string logMessage = $"[{formattedPath}:{lineNumber} ({memberName})] {message}";

        // コンソール（デバッグ出力）に出力
        Debug.WriteLine(logMessage);

        // ファイルに出力（必要な場合）
        if (shouldWriteToFile || _debugLevel < 0)
        {
            WriteToFile(logMessage);
        }
    }

    /// <summary>
    /// フォーマット付きデバッグログを出力する
    /// </summary>
    public static void Log(
        int level,
        string format,
        object[] args,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        string message = string.Format(format, args);
        Log(level, message, filePath, lineNumber, memberName);
    }

    /// <summary>
    /// 情報ログ（レベル1）
    /// </summary>
    public static void Info(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        Log(1, message, filePath, lineNumber, memberName);
    }

    /// <summary>
    /// 警告ログ（レベル2）
    /// </summary>
    public static void Warn(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        Log(2, $"[WARN] {message}", filePath, lineNumber, memberName);
    }

    /// <summary>
    /// エラーログ（レベル3、常にファイル出力）
    /// </summary>
    public static void Error(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        Log(-3, $"[ERROR] {message}", filePath, lineNumber, memberName);
    }

    /// <summary>
    /// 例外ログ（レベル3、常にファイル出力）
    /// </summary>
    public static void Exception(
        Exception ex,
        string? additionalMessage = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        var sb = new StringBuilder();
        sb.Append("[EXCEPTION] ");
        if (!string.IsNullOrEmpty(additionalMessage))
        {
            sb.Append(additionalMessage);
            sb.Append(" - ");
        }
        sb.Append(ex.GetType().Name);
        sb.Append(": ");
        sb.Append(ex.Message);
        if (ex.StackTrace != null)
        {
            sb.AppendLine();
            sb.Append("  StackTrace: ");
            sb.Append(ex.StackTrace.Replace("\n", "\n  "));
        }

        Log(-3, sb.ToString(), filePath, lineNumber, memberName);
    }

    // MARK: - ログファイルパス取得

    /// <summary>
    /// ログファイル保存先を取得（キャッシュあり）
    /// </summary>
    private static string GetLogFilePath()
    {
        if (_cachedLogFilePath != null)
        {
            return _cachedLogFilePath;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appDirectory = Path.Combine(appData, "IMEIndicatorW");
#if DEBUG
        string logFileName = "debug-d.log";
#else
        string logFileName = "debug.log";
#endif
        _cachedLogFilePath = Path.Combine(appDirectory, logFileName);

        Debug.WriteLine($"[DebugLogService] ログファイルパス: {_cachedLogFilePath}");
        return _cachedLogFilePath;
    }

    /// <summary>
    /// ログディレクトリを確保する
    /// </summary>
    private static void EnsureLogDirectory()
    {
        string logFilePath = GetLogFilePath();
        string? directory = Path.GetDirectoryName(logFilePath);

        if (directory != null && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
                Debug.WriteLine($"[DebugLogService] ログディレクトリを作成: {directory}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DebugLogService] エラー: ディレクトリ作成失敗 - {ex.Message}");
                _cachedLogFilePath = null; // 失敗したらキャッシュをクリア
            }
        }
    }

    // MARK: - ファイル出力

    /// <summary>
    /// ログをファイルに書き込む
    /// </summary>
    private static void WriteToFile(string message)
    {
        lock (_fileLock)
        {
            try
            {
                EnsureLogDirectory();

                string logFilePath = GetLogFilePath();

                // ログローテーション確認
                RotateLogFileIfNeeded(logFilePath);

                // タイムスタンプ付きメッセージ
                string timestamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff");
                string logLine = $"[{timestamp}] {message}{Environment.NewLine}";

                // ファイルに追記
                File.AppendAllText(logFilePath, logLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DebugLogService] エラー: ファイル書き込み失敗 - {ex.Message}");
            }
        }
    }

    /// <summary>
    /// ログファイルのローテーション
    /// </summary>
    private static void RotateLogFileIfNeeded(string logFilePath)
    {
        try
        {
            if (!File.Exists(logFilePath)) return;

            var fileInfo = new FileInfo(logFilePath);
            if (fileInfo.Length < MaxLogFileSize) return;

            // バックアップファイルをローテーション
            string? directory = Path.GetDirectoryName(logFilePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(logFilePath);
            string extension = Path.GetExtension(logFilePath);

            if (directory == null) return;

            // 古いバックアップを削除
            for (int i = MaxBackupFiles; i >= 1; i--)
            {
                string oldBackup = Path.Combine(directory, $"{fileNameWithoutExt}.{i}{extension}");
                string newBackup = Path.Combine(directory, $"{fileNameWithoutExt}.{i + 1}{extension}");

                if (i == MaxBackupFiles && File.Exists(oldBackup))
                {
                    File.Delete(oldBackup);
                }
                else if (File.Exists(oldBackup))
                {
                    File.Move(oldBackup, newBackup);
                }
            }

            // 現在のログファイルをバックアップ
            string firstBackup = Path.Combine(directory, $"{fileNameWithoutExt}.1{extension}");
            File.Move(logFilePath, firstBackup);

            Debug.WriteLine($"[DebugLogService] ログローテーション完了: {logFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugLogService] エラー: ログローテーション失敗 - {ex.Message}");
        }
    }

    // MARK: - ヘルパー関数

    /// <summary>
    /// ファイルパスを指定されたスタイルで整形
    /// </summary>
    private static string FormatFilePath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return "(unknown)";

        return _pathStyle switch
        {
            PathStyle.FileOnly => Path.GetFileName(fullPath),
            PathStyle.Parent1 => GetParentPath(fullPath, 1),
            PathStyle.Parent2 => GetParentPath(fullPath, 2),
            PathStyle.FullPath => fullPath,
            _ => Path.GetFileName(fullPath)
        };
    }

    /// <summary>
    /// 親ディレクトリを含むパスを取得
    /// </summary>
    private static string GetParentPath(string fullPath, int parentLevels)
    {
        var parts = fullPath.Replace('\\', '/').Split('/');
        int startIndex = Math.Max(0, parts.Length - parentLevels - 1);
        return string.Join("/", parts.Skip(startIndex));
    }

    /// <summary>
    /// ログファイルを開く（エクスプローラーで）
    /// </summary>
    public static void OpenLogFile()
    {
        try
        {
            string logFilePath = GetLogFilePath();
            if (File.Exists(logFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logFilePath,
                    UseShellExecute = true
                });
            }
            else
            {
                string? directory = Path.GetDirectoryName(logFilePath);
                if (directory != null && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugLogService] エラー: ログファイルを開けません - {ex.Message}");
        }
    }

    /// <summary>
    /// ログファイルをクリア
    /// </summary>
    public static void ClearLogFile()
    {
        lock (_fileLock)
        {
            try
            {
                string logFilePath = GetLogFilePath();
                if (File.Exists(logFilePath))
                {
                    File.WriteAllText(logFilePath, string.Empty, Encoding.UTF8);
                    Debug.WriteLine("[DebugLogService] ログファイルをクリアしました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DebugLogService] エラー: ログファイルクリア失敗 - {ex.Message}");
            }
        }
    }
}

/// <summary>
/// 簡易ログ出力用の静的クラス（エイリアス）
/// </summary>
public static class DbgLog
{
    /// <summary>
    /// デバッグログを出力
    /// </summary>
    public static void Log(
        int level,
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        DebugLogService.Log(level, message, filePath, lineNumber, memberName);
    }

    /// <summary>
    /// 情報ログ
    /// </summary>
    public static void I(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        DebugLogService.Info(message, filePath, lineNumber, memberName);
    }

    /// <summary>
    /// 警告ログ
    /// </summary>
    public static void W(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        DebugLogService.Warn(message, filePath, lineNumber, memberName);
    }

    /// <summary>
    /// エラーログ
    /// </summary>
    public static void E(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        DebugLogService.Error(message, filePath, lineNumber, memberName);
    }

    /// <summary>
    /// 例外ログ
    /// </summary>
    public static void Ex(
        Exception ex,
        string? additionalMessage = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        DebugLogService.Exception(ex, additionalMessage, filePath, lineNumber, memberName);
    }
}
