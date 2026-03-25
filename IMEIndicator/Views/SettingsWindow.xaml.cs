using System.Windows;
using IMEIndicator.Models;
using IMEIndicator.Services;
using IMEIndicator.ViewModels;

namespace IMEIndicator.Views;

/// <summary>
/// 設定ウィンドウ（ダイアログシェル）
/// タブごとの設定UIは子UserControlに委譲する
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isInitializing = true;

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
        Loaded += (_, _) => PositionAboveTaskbar();
        LoadCurrentSettings();
        InitializeChildTabs();

        _isInitializing = false;
    }

    /// <summary>
    /// ウィンドウをプライマリモニターのタスクバー直上（右下）に配置する
    /// </summary>
    private void PositionAboveTaskbar()
    {
        // DPIスケールを取得（PerMonitorV2対応）
        var dpiInfo = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double scaleX = dpiInfo.DpiScaleX;
        double scaleY = dpiInfo.DpiScaleY;

        // プライマリモニターのワーキングエリアを取得（物理ピクセル）
        var workArea = DisplayHelper.GetPrimaryWorkArea();

        // 物理ピクセルをDIPに変換
        double workRight = workArea.Right / scaleX;
        double workBottom = workArea.Bottom / scaleY;

        // ウィンドウをワーキングエリアの右下に配置（マージン10DIP）
        const double margin = 10;
        double w = double.IsNaN(Width) ? ActualWidth : Width;
        double h = double.IsNaN(Height) ? ActualHeight : Height;
        Left = workRight - w - margin;
        Top = workBottom - h - margin;
    }

    private void LoadCurrentSettings()
    {
        // 電源モードラジオボタンの初期化
        var currentMode = PowerModeService.GetCurrentMode();
        switch (currentMode)
        {
            case PowerMode.BestPowerEfficiency:
                PowerModeEfficiency.IsChecked = true;
                break;
            case PowerMode.BestPerformance:
                PowerModePerformance.IsChecked = true;
                break;
            default:
                PowerModeBalanced.IsChecked = true;
                break;
        }

        // タイトルにバージョン表示
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        TitleText.Text = $"設定 (v{version?.Major}.{version?.Minor}.{version?.Build})";
    }

    /// <summary>
    /// 子タブの初期化
    /// </summary>
    private void InitializeChildTabs()
    {
        // インジケータータブ（DataContext は MainViewModel を継承）
        IndicatorTab.Initialize(_viewModel);

        // プロセス優先度タブ
        var monitor = App.Instance.ProcessPriorityMonitor;
        if (monitor != null)
        {
            ProcessPriorityTab.Initialize(_viewModel.SettingsManager, monitor);
        }
    }

    private void PowerMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        PowerMode mode = sender switch
        {
            System.Windows.Controls.RadioButton rb when rb == PowerModeEfficiency => PowerMode.BestPowerEfficiency,
            System.Windows.Controls.RadioButton rb when rb == PowerModePerformance => PowerMode.BestPerformance,
            _ => PowerMode.Balanced
        };

        PowerModeService.SetMode(mode);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ProcessPriorityTab.SaveSettings();
        _viewModel.SaveSettings();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ProcessPriorityTab.SaveSettings();
        _viewModel.SaveSettings();
        base.OnClosing(e);
    }
}
