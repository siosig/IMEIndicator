using System.Windows;
using System.Windows.Controls;
using IMEIndicator.ViewModels;

namespace IMEIndicator.Views;

/// <summary>
/// インジケーター設定タブ（UserControl）
/// DataContext は親ウィンドウから MainViewModel を継承する
/// </summary>
public partial class IndicatorSettingsTab : UserControl
{
    private MainViewModel? _viewModel;
    private bool _isInitializing = true;

    public IndicatorSettingsTab()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 親ウィンドウから初期化を行う
    /// </summary>
    public void Initialize(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        MouseIndicatorVisibleCheck.IsChecked = viewModel.MouseCursorIndicatorViewModel.IsVisible;
        _isInitializing = false;
    }

    private void MouseIndicatorVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _viewModel == null) return;
        _viewModel.MouseCursorIndicatorViewModel.IsVisible = MouseIndicatorVisibleCheck.IsChecked == true;
        _viewModel.SaveSettings();
    }
}
