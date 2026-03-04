using System.Windows;
using System.Windows.Media;
using IMEIndicatorClock.Services;
using IMEIndicatorClock.ViewModels;

namespace IMEIndicatorClock.Views;

/// <summary>
/// 設定ウィンドウ（日本語IMEカーソルインジケーター専用）
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
        LoadCurrentSettings();

        _isInitializing = false;
    }

    private void LoadCurrentSettings()
    {
        // マウスインジケーター表示状態
        MouseIndicatorVisibleCheck.IsChecked = _viewModel.MouseCursorIndicatorViewModel.IsVisible;
        HideWhenImeOffCheck.IsChecked = _viewModel.MouseCursorIndicatorViewModel.HideWhenImeOff;

        // IME ON/OFF 色・文字
        var settings = _viewModel.SettingsManager.Settings;
        ImeOnColorText.Text = settings.ImeOnColor;
        ImeOnTextInput.Text = settings.ImeOnText;
        ImeOffColorText.Text = settings.ImeOffColor;
        ImeOffTextInput.Text = settings.ImeOffText;
        UpdateColorPreview(ImeOnColorPreview, settings.ImeOnColor);
        UpdateColorPreview(ImeOffColorPreview, settings.ImeOffColor);

        // バージョン情報
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        LblVersion.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

        // デバッグモード
        DebugModeCheck.IsChecked = IMEMonitor.DebugMode;
    }

    private static void UpdateColorPreview(System.Windows.Controls.Border preview, string hex)
    {
        if (ColorHelper.IsValidHexColor(hex))
            preview.Background = new SolidColorBrush(ColorHelper.ParseColor(hex));
        else
            preview.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void ImeOnColor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var hex = ImeOnColorText.Text;
        UpdateColorPreview(ImeOnColorPreview, hex);
        if (!ColorHelper.IsValidHexColor(hex)) return;
        _viewModel.SettingsManager.Settings.ImeOnColor = hex;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void ImeOnText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var text = ImeOnTextInput.Text;
        if (string.IsNullOrEmpty(text)) return;
        _viewModel.SettingsManager.Settings.ImeOnText = text;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void ImeOffColor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var hex = ImeOffColorText.Text;
        UpdateColorPreview(ImeOffColorPreview, hex);
        if (!ColorHelper.IsValidHexColor(hex)) return;
        _viewModel.SettingsManager.Settings.ImeOffColor = hex;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void ImeOffText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var text = ImeOffTextInput.Text;
        if (string.IsNullOrEmpty(text)) return;
        _viewModel.SettingsManager.Settings.ImeOffText = text;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void MouseIndicatorVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _viewModel.MouseCursorIndicatorViewModel.IsVisible = MouseIndicatorVisibleCheck.IsChecked == true;
        _viewModel.SaveSettings();
    }

    private void HideWhenImeOff_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _viewModel.MouseCursorIndicatorViewModel.HideWhenImeOff = HideWhenImeOffCheck.IsChecked == true;
        _viewModel.SaveSettings();
    }

    private void DebugMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        IMEMonitor.DebugMode = DebugModeCheck.IsChecked == true;
        _viewModel.SaveSettings();
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "すべての設定をデフォルトに戻しますか？",
            "設定のリセット",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ResetToDefaults();
            _isInitializing = true;
            LoadCurrentSettings();
            _isInitializing = false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettings();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.SaveSettings();
        base.OnClosing(e);
    }
}
