using System.Diagnostics;
using System.IO;

namespace IMEIndicatorClock.Services;

/// <summary>
/// Windows起動時の自動起動を管理するサービス
/// タスクスケジューラを使用して管理者権限でUACなしに起動
/// </summary>
public static class StartupManager
{
    private static string TaskName => AppConstants.AppName;

    /// <summary>
    /// 自動起動が有効かどうかを取得
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                var result = RunSchtasks("/query /tn \"" + TaskName + "\"", waitForExit: true);
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 自動起動の有効/無効を設定
    /// </summary>
    public static bool SetEnabled(bool enable)
    {
        try
        {
            if (enable)
            {
                var exePath = GetExecutablePath();
                DbgLog.I($"実行ファイルパス: {exePath}");

                if (string.IsNullOrEmpty(exePath))
                {
                    DbgLog.E("実行ファイルのパスが取得できません");
                    return false;
                }

                // 既存のタスクを削除してから作成（/f で強制上書き）
                var args = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
                DbgLog.I($"schtasks引数: {args}");
                var result = RunSchtasks(args, waitForExit: true);

                if (result.ExitCode != 0)
                {
                    DbgLog.E($"タスク作成失敗: ExitCode={result.ExitCode}, Error={result.ErrorOutput}");
                    return false;
                }

                DbgLog.I("自動起動を有効にしました");
                return true;
            }
            else
            {
                var args = $"/delete /tn \"{TaskName}\" /f";
                var result = RunSchtasks(args, waitForExit: true);

                // タスクが存在しない場合もエラーになるが、削除目的なので成功扱い
                DbgLog.I("自動起動を無効にしました");
                return true;
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "自動起動設定エラー");
            return false;
        }
    }

    /// <summary>
    /// 実行ファイルのパスを取得（デバッグ時も正しいパスを返す）
    /// </summary>
    private static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;

        // dotnet.exe 経由で実行されている場合（デバッグ時）
        if (processPath != null && processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            // AppContext.BaseDirectory + アセンブリ名.exe を使用
            var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            var exePath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.exe");

            if (File.Exists(exePath))
            {
                return exePath;
            }

            DbgLog.W($"デバッグ用EXEが見つかりません: {exePath}");
            return null;
        }

        return processPath;
    }

    private static ProcessResult RunSchtasks(string args, bool waitForExit)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return new ProcessResult { ExitCode = -1, ErrorOutput = "プロセス起動失敗" };
        }

        string errorOutput = "";
        if (waitForExit)
        {
            errorOutput = process.StandardError.ReadToEnd();
            process.WaitForExit(5000); // 5秒タイムアウト
        }

        return new ProcessResult { ExitCode = process.ExitCode, ErrorOutput = errorOutput };
    }

    private struct ProcessResult
    {
        public int ExitCode { get; set; }
        public string ErrorOutput { get; set; }
    }
}
