using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.ViewModels;

/// <summary>
/// アプリケーションのメインViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    public SettingsManager SettingsManager => _settingsManager;
    public IMEIndicatorViewModel IMEIndicatorViewModel { get; }
    public ClockViewModel ClockViewModel { get; }
    public MouseCursorIndicatorViewModel MouseCursorIndicatorViewModel { get; }

    public MainViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        IMEIndicatorViewModel = new IMEIndicatorViewModel(settingsManager);
        ClockViewModel = new ClockViewModel(settingsManager);
        MouseCursorIndicatorViewModel = new MouseCursorIndicatorViewModel(settingsManager);
    }

    /// <summary>
    /// IME状態を更新する
    /// </summary>
    public void UpdateIMEState(LanguageInfo languageInfo)
    {
        DbgLog.Log(5, $"[MainVM] UpdateIMEState開始: {languageInfo.Language}/{languageInfo.IsIMEOn}");
        try
        {
            IMEIndicatorViewModel.UpdateState(languageInfo);
            DbgLog.Log(6, "[MainVM] IMEIndicatorViewModel.UpdateState完了");
            ClockViewModel.UpdateIMEState(languageInfo);
            DbgLog.Log(6, "[MainVM] ClockViewModel.UpdateIMEState完了");
            MouseCursorIndicatorViewModel.UpdateState(languageInfo);
            DbgLog.Log(6, "[MainVM] MouseCursorIndicatorViewModel.UpdateState完了");
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "[MainVM] UpdateIMEState例外");
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

        // ViewModelを更新
        IMEIndicatorViewModel.ReloadSettings();
        ClockViewModel.ReloadSettings();
        MouseCursorIndicatorViewModel.ReloadSettings();
    }

    /// <summary>
    /// IMEインジケーターの位置をプリセットに設定
    /// </summary>
    [RelayCommand]
    public void SetIMEIndicatorPosition(PositionPreset preset)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var displayIndex = IMEIndicatorViewModel.DisplayIndex;
        if (displayIndex >= screens.Length) displayIndex = 0;
        var screen = screens[displayIndex].WorkingArea;

        var size = IMEIndicatorViewModel.Size;
        const double offset = 50;

        var (x, y) = preset switch
        {
            PositionPreset.TopLeft => (screen.Left + offset, screen.Top + offset),
            PositionPreset.TopRight => (screen.Right - size - offset, screen.Top + offset),
            PositionPreset.BottomLeft => (screen.Left + offset, screen.Bottom - size - offset),
            PositionPreset.BottomRight => (screen.Right - size - offset, screen.Bottom - size - offset),
            _ => (screen.Left + offset, screen.Top + offset)
        };

        IMEIndicatorViewModel.PositionX = x;
        IMEIndicatorViewModel.PositionY = y;
    }

    /// <summary>
    /// 時計の位置をプリセットに設定
    /// </summary>
    [RelayCommand]
    public void SetClockPosition(PositionPreset preset)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var displayIndex = ClockViewModel.DisplayIndex;
        if (displayIndex >= screens.Length) displayIndex = 0;
        var screen = screens[displayIndex].WorkingArea;

        var width = ClockViewModel.Width;
        var height = ClockViewModel.Height;
        const double offset = 0;

        var (x, y) = preset switch
        {
            PositionPreset.TopLeft => (screen.Left + offset, screen.Top + offset),
            PositionPreset.TopRight => (screen.Right - width - offset, screen.Top + offset),
            PositionPreset.BottomLeft => (screen.Left + offset, screen.Bottom - height - offset),
            PositionPreset.BottomRight => (screen.Right - width - offset, screen.Bottom - height - offset),
            _ => (screen.Left + offset, screen.Top + offset)
        };

        ClockViewModel.PositionX = x;
        ClockViewModel.PositionY = y;
    }

    /// <summary>
    /// IMEインジケーターを指定したディスプレイに移動（デフォルト位置: 左上）
    /// </summary>
    public void MoveIMEIndicatorToDisplay(int displayIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (displayIndex >= screens.Length) displayIndex = 0;

        IMEIndicatorViewModel.DisplayIndex = displayIndex;
        SetIMEIndicatorPosition(PositionPreset.TopLeft);
    }

    /// <summary>
    /// 時計を指定したディスプレイに移動（デフォルト位置: 右上）
    /// </summary>
    public void MoveClockToDisplay(int displayIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (displayIndex >= screens.Length) displayIndex = 0;

        ClockViewModel.DisplayIndex = displayIndex;
        SetClockPosition(PositionPreset.TopRight);
    }
}
