using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IMEIndicator.Models;
using IMEIndicator.Services;
using IMEIndicator.ViewModels;

namespace IMEIndicator.Views;

/// <summary>
/// 設定ウィンドウ（日本語IMEカーソルインジケーター専用）
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ProcessPrioritySettingsViewModel? _priorityViewModel;
    private bool _isInitializing = true;

    /// <summary>
    /// プロセス名補完用：実行中プロセス名のキャッシュ
    /// </summary>
    private string[]? _processNameCache;

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
        LoadCurrentSettings();
        InitializePriorityTab();

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
    }

    private static void UpdateColorPreview(Border preview, string hex)
    {
        if (ColorHelper.IsValidHexColor(hex))
            preview.Background = new SolidColorBrush(ColorHelper.ParseColor(hex));
        else
            preview.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void ImeOnColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var hex = ImeOnColorText.Text;
        UpdateColorPreview(ImeOnColorPreview, hex);
        if (!ColorHelper.IsValidHexColor(hex)) return;
        _viewModel.SettingsManager.Settings.ImeOnColor = hex;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void ImeOnText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var text = ImeOnTextInput.Text;
        if (string.IsNullOrEmpty(text)) return;
        _viewModel.SettingsManager.Settings.ImeOnText = text;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void ImeOffColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        var hex = ImeOffColorText.Text;
        UpdateColorPreview(ImeOffColorPreview, hex);
        if (!ColorHelper.IsValidHexColor(hex)) return;
        _viewModel.SettingsManager.Settings.ImeOffColor = hex;
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
        _viewModel.SaveSettings();
    }

    private void ImeOffText_TextChanged(object sender, TextChangedEventArgs e)
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
        App.Instance.ApplyCurrentVisibility();
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

    // ==================== プロセス優先度タブ ====================

    private void InitializePriorityTab()
    {
        var monitor = App.Instance.ProcessPriorityMonitor;
        if (monitor == null) return;

        _priorityViewModel = new ProcessPrioritySettingsViewModel(_viewModel.SettingsManager, monitor);
        PriorityRulesGrid.ItemsSource = _priorityViewModel.Rules;

        // ポーリング間隔入力欄の初期値
        PollingIntervalInput.Text = _priorityViewModel.PollingIntervalSeconds.ToString();

        // ComboBox 列に PriorityLevel の選択肢を設定（列インデックス: 有効=0, プロセス名=1, 優先度=2）
        if (PriorityRulesGrid.Columns[2] is DataGridComboBoxColumn comboColumn)
            comboColumn.ItemsSource = ProcessPrioritySettingsViewModel.PriorityLevels;

        // プロセス名キャッシュを構築
        RefreshProcessNameCache();
    }

    private void AddPriorityRule_Click(object sender, RoutedEventArgs e)
    {
        _priorityViewModel?.AddRuleCommand.Execute(null);
    }

    private void RemovePriorityRule_Click(object sender, RoutedEventArgs e)
    {
        var selected = PriorityRulesGrid.SelectedItem as ProcessPriorityRule;
        _priorityViewModel?.RemoveRuleCommand.Execute(selected);
    }

    private void PriorityRulesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.InvokeAsync(() => _priorityViewModel?.SaveAndSync(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void PollingInterval_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _priorityViewModel == null) return;
        if (int.TryParse(PollingIntervalInput.Text, out int value) && value >= 1)
        {
            _priorityViewModel.PollingIntervalSeconds = Math.Clamp(value, 1, 1800);
            _priorityViewModel.SaveAndSync();
        }
    }

    // ==================== プロセス名前方一致補完 ====================

    private void RefreshProcessNameCache()
    {
        try
        {
            _processNameCache = System.Diagnostics.Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName; }
                    catch { return null; }
                    finally { p.Dispose(); }
                })
                .Where(n => n != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch
        {
            _processNameCache = [];
        }
    }

    /// <summary>
    /// DataTemplate 内の Grid から Popup を取得するヘルパー
    /// </summary>
    private static (Popup? popup, System.Windows.Controls.ListBox? listBox) FindPopupAndListBox(TextBox textBox)
    {
        if (textBox.Parent is not Grid parent) return (null, null);

        var popup = parent.Children.OfType<Popup>().FirstOrDefault();
        if (popup?.Child is not Border border) return (popup, null);

        return (popup, border.Child as System.Windows.Controls.ListBox);
    }

    private void ProcessNameInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (_processNameCache == null || _processNameCache.Length == 0) return;

        var text = textBox.Text?.Trim() ?? string.Empty;
        var (popup, listBox) = FindPopupAndListBox(textBox);

        if (text.Length == 0 || popup == null || listBox == null)
        {
            if (popup != null) popup.IsOpen = false;
            return;
        }

        var matches = _processNameCache
            .Where(n => n!.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToArray();

        if (matches.Length == 0)
        {
            popup.IsOpen = false;
            return;
        }

        listBox.ItemsSource = matches;
        listBox.SelectedIndex = -1;
        popup.IsOpen = true;
    }

    /// <summary>
    /// キーボード操作: 上下で候補選択、Enterで確定、Escでキャンセル
    /// </summary>
    private void ProcessNameInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var (popup, listBox) = FindPopupAndListBox(textBox);
        if (popup == null || listBox == null || !popup.IsOpen) return;

        switch (e.Key)
        {
            case System.Windows.Input.Key.Down:
                if (listBox.SelectedIndex < listBox.Items.Count - 1)
                    listBox.SelectedIndex++;
                e.Handled = true;
                break;

            case System.Windows.Input.Key.Up:
                if (listBox.SelectedIndex > 0)
                    listBox.SelectedIndex--;
                e.Handled = true;
                break;

            case System.Windows.Input.Key.Enter:
                if (listBox.SelectedItem != null)
                {
                    ApplyProcessNameSelection(textBox, popup, listBox);
                    e.Handled = true;
                }
                break;

            case System.Windows.Input.Key.Escape:
                popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// マウスクリックで候補を確定
    /// </summary>
    private void ProcessNameSuggestion_MouseClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox || listBox.SelectedItem == null) return;

        var (popup, _) = FindPopupFromListBox(listBox);
        if (popup?.PlacementTarget is TextBox textBox)
        {
            ApplyProcessNameSelection(textBox, popup, listBox);
        }
    }

    /// <summary>
    /// TextBox のフォーカスが外れたら Popup を閉じる
    /// </summary>
    private void ProcessNameInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        // ListBox へのクリック中にフォーカスが移る場合があるため少し遅延
        Dispatcher.InvokeAsync(() =>
        {
            var (popup, _) = FindPopupAndListBox(textBox);
            if (popup != null) popup.IsOpen = false;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 選択した候補を TextBox に反映して Popup を閉じる
    /// </summary>
    private void ApplyProcessNameSelection(TextBox textBox, Popup popup, System.Windows.Controls.ListBox listBox)
    {
        var selected = listBox.SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(selected)) return;

        textBox.TextChanged -= ProcessNameInput_TextChanged;
        textBox.Text = selected;
        textBox.CaretIndex = textBox.Text.Length;
        textBox.TextChanged += ProcessNameInput_TextChanged;

        popup.IsOpen = false;
        listBox.SelectedItem = null;

        Dispatcher.InvokeAsync(() => _priorityViewModel?.SaveAndSync(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// ListBox から親の Popup を取得するヘルパー
    /// </summary>
    private static (Popup? popup, TextBox? textBox) FindPopupFromListBox(System.Windows.Controls.ListBox listBox)
    {
        Popup? popup = null;
        if (listBox.Parent is Border border)
            popup = System.Windows.Media.VisualTreeHelper.GetParent(border) as Popup;

        var textBox = popup?.PlacementTarget as TextBox;
        return (popup, textBox);
    }

    // ==================== 共通 ====================

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _priorityViewModel?.SaveAndSync();
        _viewModel.SaveSettings();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _priorityViewModel?.SaveAndSync();
        _viewModel.SaveSettings();
        base.OnClosing(e);
    }
}
