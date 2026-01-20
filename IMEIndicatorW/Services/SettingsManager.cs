using System.IO;
using System.Text.Json;
using System.Windows;
using IMEIndicatorClock.Models;

namespace IMEIndicatorClock.Services;

/// <summary>
/// 設定の読み書きを管理するサービス
/// </summary>
public class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppConstants.AppName
    );

#if DEBUG
    private static readonly string SettingsFileName = "settings-d.json";
#else
    private static readonly string SettingsFileName = "settings.json";
#endif

    private static readonly string SettingsFilePath = Path.Combine(
        SettingsDirectory,
        SettingsFileName
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 現在の設定
    /// </summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// 最後に発生したエラーメッセージ
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 設定を読み込む
    /// </summary>
    public bool Load()
    {
        LastError = null;
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                
                if (settings != null)
                {
                    Settings = settings;

                    // null チェックと初期化（古い設定ファイルからの読み込み対応）
                    Settings.Clock ??= new ClockSettings();
                    Settings.IMEIndicator ??= new IMEIndicatorSettings();
                    Settings.MouseCursorIndicator ??= new MouseCursorIndicatorSettings();
                    Settings.Debug ??= new DebugSettings();

                    DbgLog.I($"設定を読み込みました: {SettingsFilePath}");
                    DbgLog.Log(3, $"SettingsManager.Load: Clock.PositionX={Settings.Clock.PositionX}, Clock.PositionY={Settings.Clock.PositionY}");

                    // デバッグレベルを設定から反映
                    DebugLogService.DebugLevel = Settings.Debug.LogLevel;
                    return true;
                }
            }
            else
            {
                DbgLog.I("設定ファイルが存在しないため、デフォルト設定を使用します");
                Settings = new AppSettings();
                return true;
            }
        }
        catch (JsonException ex)
        {
            LastError = $"設定ファイルの形式が不正です: {ex.Message}";
            DbgLog.E($"設定の読み込みに失敗（JSON形式エラー）: {ex.Message}");
            NotifyLoadError();
            Settings = new AppSettings();
        }
        catch (IOException ex)
        {
            LastError = $"設定ファイルの読み込みに失敗しました: {ex.Message}";
            DbgLog.E($"設定の読み込みに失敗（I/Oエラー）: {ex.Message}");
            NotifyLoadError();
            Settings = new AppSettings();
        }
        catch (Exception ex)
        {
            LastError = $"設定の読み込み中に予期しないエラーが発生しました: {ex.Message}";
            DbgLog.Ex(ex, "設定の読み込みに失敗");
            NotifyLoadError();
            Settings = new AppSettings();
        }

        return false;
    }

    /// <summary>
    /// 設定を保存する
    /// </summary>
    public bool Save()
    {
        LastError = null;
        try
        {
            // ディレクトリが存在しない場合は作成
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
                DbgLog.I($"設定ディレクトリを作成しました: {SettingsDirectory}");
            }

            // 現在のデバッグレベルを設定に保存
            Settings.Debug ??= new DebugSettings();
            Settings.Debug.LogLevel = DebugLogService.DebugLevel;

            // 保存前の時計位置をログ
            DbgLog.Log(3, $"SettingsManager.Save: Clock.PositionX={Settings.Clock.PositionX}, Clock.PositionY={Settings.Clock.PositionY}");

            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
            DbgLog.Log(3, $"設定を保存しました: {SettingsFilePath}");
            return true;
        }
        catch (IOException ex)
        {
            LastError = $"設定ファイルの保存に失敗しました: {ex.Message}";
            DbgLog.E($"設定の保存に失敗（I/Oエラー）: {ex.Message}");
            NotifySaveError();
        }
        catch (Exception ex)
        {
            LastError = $"設定の保存中に予期しないエラーが発生しました: {ex.Message}";
            DbgLog.Ex(ex, "設定の保存に失敗");
            NotifySaveError();
        }

        return false;
    }

    /// <summary>
    /// 設定をデフォルトにリセットする
    /// </summary>
    public void Reset()
    {
        Settings = new AppSettings();
        Save();
        DbgLog.I("設定をデフォルトにリセットしました");
    }

    /// <summary>
    /// 設定をデフォルトにリセットする（Reset()のエイリアス）
    /// </summary>
    public void ResetToDefaults() => Reset();

    /// <summary>
    /// 設定ファイルのパスを取得
    /// </summary>
    public static string GetSettingsFilePath() => SettingsFilePath;

    /// <summary>
    /// 設定ディレクトリを開く
    /// </summary>
    public static void OpenSettingsDirectory()
    {
        try
        {
            if (Directory.Exists(SettingsDirectory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SettingsDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            DbgLog.E($"設定ディレクトリを開けません: {ex.Message}");
        }
    }

    /// <summary>
    /// 読み込みエラーをユーザーに通知
    /// </summary>
    private void NotifyLoadError()
    {
        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    $"設定ファイルの読み込みに失敗しました。\nデフォルト設定を使用します。\n\n{LastError}",
                    "IMEIndicatorW - 警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }));
        }
    }

    /// <summary>
    /// 保存エラーをユーザーに通知
    /// </summary>
    private void NotifySaveError()
    {
        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    $"設定の保存に失敗しました。\n\n{LastError}",
                    "IMEIndicatorW - エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }));
        }
    }
}
