using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// 設定の読み書きを管理するサービス
/// </summary>
public class SettingsManager
{
    private static readonly string DefaultSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppConstants.AppName
    );

#if DEBUG
    private const string DefaultSettingsFileName = "settings-d.json";
#else
    private const string DefaultSettingsFileName = "settings.json";
#endif

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// デフォルトコンストラクタ（本番用）
    /// </summary>
    public SettingsManager() : this(null) { }

    /// <summary>
    /// テスト用コンストラクタ（設定ディレクトリを注入可能）
    /// </summary>
    public SettingsManager(string? settingsDirectory)
    {
        _settingsDirectory = settingsDirectory ?? DefaultSettingsDirectory;
        _settingsFilePath = Path.Combine(_settingsDirectory, DefaultSettingsFileName);
    }

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

        // 前回クラッシュで残った .tmp ファイルをクリーンアップ
        CleanupStaleTempFiles();

        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                if (settings != null)
                {
                    Settings = settings;

                    // null チェックと初期化
                    Settings.MouseCursorIndicator ??= new MouseCursorIndicatorSettings();

                    // 範囲外の値をクランプ
                    ValidateAndClampSettings();

                    // デバッグレベルを設定から反映
                    return true;
                }
            }
            else
            {
                Settings = new AppSettings();
                return true;
            }
        }
        catch (JsonException ex)
        {
            LastError = $"設定ファイルの形式が不正です: {ex.Message}";
            NotifyLoadError();
            Settings = new AppSettings();
        }
        catch (IOException ex)
        {
            LastError = $"設定ファイルの読み込みに失敗しました: {ex.Message}";
            NotifyLoadError();
            Settings = new AppSettings();
        }
        catch (Exception ex)
        {
            LastError = $"設定の読み込み中に予期しないエラーが発生しました: {ex.Message}";
            NotifyLoadError();
            Settings = new AppSettings();
        }

        return false;
    }

    /// <summary>
    /// 設定値を有効な範囲にクランプする
    /// </summary>
    public void ValidateAndClampSettings()
    {
        var s = Settings;
        s.MouseCursorIndicator.Opacity = Math.Clamp(s.MouseCursorIndicator.Opacity, 0.1, 1.0);
        s.MouseCursorIndicator.Size = Math.Clamp(s.MouseCursorIndicator.Size, 20, 100);

        // ProcessPriorityRules のバリデーション
        s.ProcessPriorityRules ??= [];
        foreach (var rule in s.ProcessPriorityRules)
        {
            rule.IntervalSeconds = Math.Max(10, rule.IntervalSeconds);
            rule.MaxBackoffExponent = Math.Clamp(rule.MaxBackoffExponent, 0, 10);
        }
    }

    /// <summary>
    /// 設定を保存する
    /// </summary>
    public bool Save()
    {
        LastError = null;
        try
        {
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }

            var json = JsonSerializer.Serialize(Settings, JsonOptions);

            // アトミック保存: .tmp に書き込み → File.Move で上書き
            var tmpPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _settingsFilePath, overwrite: true);
            return true;
        }
        catch (IOException ex)
        {
            LastError = $"設定ファイルの保存に失敗しました: {ex.Message}";
            NotifySaveError();
        }
        catch (Exception ex)
        {
            LastError = $"設定の保存中に予期しないエラーが発生しました: {ex.Message}";
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
    }

    /// <summary>
    /// 設定をデフォルトにリセットする（Reset()のエイリアス）
    /// </summary>
    public void ResetToDefaults() => Reset();

    /// <summary>
    /// 設定ファイルのパスを取得
    /// </summary>
    public string GetSettingsFilePath() => _settingsFilePath;

    /// <summary>
    /// 設定ディレクトリを開く
    /// </summary>
    public void OpenSettingsDirectory()
    {
        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _settingsDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager] OpenSettingsDirectory failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 前回クラッシュで残った .tmp ファイルを削除
    /// </summary>
    private void CleanupStaleTempFiles()
    {
        try
        {
            var tmpPath = _settingsFilePath + ".tmp";
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] Cleaned up stale temp file: {tmpPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager] Failed to cleanup temp file: {ex.Message}");
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
                    $"{AppConstants.AppName} - 警告",
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
                    $"{AppConstants.AppName} - エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }));
        }
    }
}
