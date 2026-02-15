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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// デフォルトコンストラクタ（本番用）
    /// </summary>
    public SettingsManager() : this(null) { }

    /// <summary>
    /// テスト用コンストラクタ（設定ディレクトリを注入可能）
    /// </summary>
    internal SettingsManager(string? settingsDirectory)
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
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                if (settings != null)
                {
                    Settings = settings;

                    // null チェックと初期化（古い設定ファイルからの読み込み対応）
                    Settings.Clock ??= new ClockSettings();
                    Settings.IMEIndicator ??= new IMEIndicatorSettings();
                    Settings.MouseCursorIndicator ??= new MouseCursorIndicatorSettings();
                    Settings.Debug ??= new DebugSettings();

                    // 範囲外の値をクランプ
                    ValidateAndClampSettings();

                    DbgLog.I($"設定を読み込みました: {_settingsFilePath}");
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
    /// 設定値を有効な範囲にクランプする
    /// </summary>
    internal void ValidateAndClampSettings()
    {
        var s = Settings;
        s.IMEIndicator.Opacity = Math.Clamp(s.IMEIndicator.Opacity, 0.1, 1.0);
        s.IMEIndicator.Size = Math.Clamp(s.IMEIndicator.Size, 16, 512);
        s.IMEIndicator.FontSizeRatio = Math.Clamp(s.IMEIndicator.FontSizeRatio, 0.1, 1.0);
        s.IMEIndicator.PixelVerificationIntervalMs = Math.Max(0, s.IMEIndicator.PixelVerificationIntervalMs);

        s.Clock.Opacity = Math.Clamp(s.Clock.Opacity, 0.1, 1.0);
        s.Clock.Width = Math.Clamp(s.Clock.Width, 50, 2000);
        s.Clock.Height = Math.Clamp(s.Clock.Height, 30, 2000);
        s.Clock.FontSize = Math.Clamp(s.Clock.FontSize, 6, 200);
        s.Clock.AnalogClockSize = Math.Clamp(s.Clock.AnalogClockSize, 100, 500);

        s.MouseCursorIndicator.Opacity = Math.Clamp(s.MouseCursorIndicator.Opacity, 0.1, 1.0);
        s.MouseCursorIndicator.Size = Math.Clamp(s.MouseCursorIndicator.Size, 8, 256);

        s.Debug.PollingInterval = Math.Clamp(s.Debug.PollingInterval, 50, 10000);
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
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
                DbgLog.I($"設定ディレクトリを作成しました: {_settingsDirectory}");
            }

            // 現在のデバッグレベルを設定に保存
            Settings.Debug ??= new DebugSettings();
            Settings.Debug.LogLevel = DebugLogService.DebugLevel;

            // 保存前の時計位置をログ
            DbgLog.Log(3, $"SettingsManager.Save: Clock.PositionX={Settings.Clock.PositionX}, Clock.PositionY={Settings.Clock.PositionY}");

            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
            DbgLog.Log(3, $"設定を保存しました: {_settingsFilePath}");
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
