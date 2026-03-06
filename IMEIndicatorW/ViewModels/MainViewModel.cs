using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.ViewModels;

/// <summary>
/// アプリケーションのメインViewModel（マウスカーソルインジケーター専用）
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    public SettingsManager SettingsManager => _settingsManager;
    public MouseCursorIndicatorViewModel MouseCursorIndicatorViewModel { get; }

    public MainViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        MouseCursorIndicatorViewModel = new MouseCursorIndicatorViewModel(settingsManager);
    }

    /// <summary>
    /// IME状態を更新する
    /// </summary>
    public void UpdateIMEState(LanguageInfo languageInfo)
    {
        try
        {
            MouseCursorIndicatorViewModel.UpdateState(languageInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] UpdateIMEState failed: {ex.Message}");
        }
    }

    /// <summary>
    /// カーソル位置を更新
    /// </summary>
    public void UpdateCursorPosition(int x, int y)
    {
        MouseCursorIndicatorViewModel.UpdateCursorPosition(x, y);
    }

    /// <summary>
    /// 設定を保存する
    /// </summary>
    public void SaveSettings()
    {
        _settingsManager.Save();
    }

    /// <summary>
    /// 設定をデフォルトにリセット
    /// </summary>
    public void ResetToDefaults()
    {
        _settingsManager.ResetToDefaults();
        MouseCursorIndicatorViewModel.ReloadSettings();
    }
}
