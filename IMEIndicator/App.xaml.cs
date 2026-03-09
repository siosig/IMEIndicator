using System.Diagnostics;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using IMEIndicator.Services;
using IMEIndicator.ViewModels;
using IMEIndicator.Views;

namespace IMEIndicator;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private IMEMonitor? _imeMonitor;
    private MouseCursorIndicatorWindow? _mouseCursorIndicatorWindow;
    private SettingsManager? _settingsManager;
    private MainViewModel? _mainViewModel;

    // 外部からアクセス可能なインスタンス
    public static App Instance => (App)Current;
    public MouseCursorIndicatorWindow? MouseCursorIndicatorWindow => _mouseCursorIndicatorWindow;
    public SettingsManager? SettingsManager => _settingsManager;
    public IMEMonitor? IMEMonitor => _imeMonitor;
    public MainViewModel? MainViewModel => _mainViewModel;

    /// <summary>
    /// 設定ウィンドウが開いているかどうか
    /// </summary>
    public bool IsSettingsWindowOpen { get; set; } = false;

    /// <summary>
    /// マウスインジケーターの表示切替
    /// </summary>
    public void SetMouseIndicatorVisible(bool visible)
    {
        if (_mouseCursorIndicatorWindow == null || _settingsManager == null || _mainViewModel == null) return;
        _settingsManager.Settings.MouseCursorIndicator.IsVisible = visible;
        _mainViewModel.MouseCursorIndicatorViewModel.IsVisible = visible;
        if (visible) _mouseCursorIndicatorWindow.Show();
        else _mouseCursorIndicatorWindow.Hide();
    }

    /// <summary>
    /// 現在のIME状態に基づいてウィンドウ表示を即時反映（設定変更時に呼ぶ）
    /// </summary>
    public void ApplyCurrentVisibility()
    {
        if (_imeMonitor == null) return;
        ApplyWindowVisibility(_imeMonitor.CurrentState);
    }

    /// <summary>
    /// 設定ウィンドウを開く
    /// </summary>
    public void OpenSettingsWindow()
    {
        if (_mainViewModel == null) return;

        // 既に開いている場合は何もしない
        if (IsSettingsWindowOpen) return;

        var settingsWindow = new SettingsWindow(_mainViewModel);
        settingsWindow.Show();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // デバッグログの初期化
#if DEBUG
#else
#endif

        try
        {
            // 設定の読み込み
            _settingsManager = new SettingsManager();
            _settingsManager.Load();

#if DEBUG
#endif

            // ViewModelの初期化
            _mainViewModel = new MainViewModel(_settingsManager);

            // IMEモニターの開始
            _imeMonitor = new IMEMonitor();
            _imeMonitor.IMEStateChanged += OnIMEStateChanged;
            _imeMonitor.CursorPositionChanged += OnCursorPositionChanged;
            _imeMonitor.Start();

            // マウスカーソルインジケーターウィンドウ
            _mouseCursorIndicatorWindow = new MouseCursorIndicatorWindow(_mainViewModel.MouseCursorIndicatorViewModel);
            // HideWhenImeOff を含む全設定を考慮した初期表示
            ApplyWindowVisibility(_imeMonitor.CurrentState);

            // システムトレイアイコン
            InitializeTrayIcon();

            // 設定ウィンドウの表示
#if DEBUG
            var settingsWindow = new SettingsWindow(_mainViewModel);
            settingsWindow.Show();
#else
            if (_settingsManager.Settings.IsFirstLaunch)
            {
                _settingsManager.Settings.IsFirstLaunch = false;
                _settingsManager.Save();

                var settingsWindow = new SettingsWindow(_mainViewModel);
                settingsWindow.Show();
            }
#endif
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"アプリケーションの起動中にエラーが発生しました。\n\n{ex.Message}\n\n{ex.StackTrace}",
                $"{AppConstants.AppName} - エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = LoadIcon(),
            ToolTipText = AppConstants.AppName,
            ContextMenu = CreateContextMenu()
        };
    }

    private System.Drawing.Icon LoadIcon()
    {
#if DEBUG
        var iconFileName = "app-debug.ico";
#else
        var iconFileName = "app.ico";
#endif
        try
        {
            var uri = new Uri($"pack://application:,,,/{iconFileName}", UriKind.Absolute);
            var streamInfo = GetResourceStream(uri);
            if (streamInfo != null)
            {
                return new System.Drawing.Icon(streamInfo.Stream);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Failed to load icon from resource, trying external file: {ex.Message}");
        }

        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconFileName);
        if (System.IO.File.Exists(iconPath))
        {
            return new System.Drawing.Icon(iconPath);
        }

        return System.Drawing.SystemIcons.Application;
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // マウスカーソルインジケーター表示/非表示
        var mouseIndicatorItem = new System.Windows.Controls.MenuItem
        {
            Header = "表示切替",
            IsCheckable = true,
            IsChecked = _settingsManager?.Settings.MouseCursorIndicator.IsVisible ?? true
        };
        mouseIndicatorItem.Click += (s, e) =>
        {
            var item = (System.Windows.Controls.MenuItem)s!;
            SetMouseIndicatorVisible(item.IsChecked);
            _settingsManager?.Save();
        };
        menu.Items.Add(mouseIndicatorItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // 設定
        var settingsItem = new System.Windows.Controls.MenuItem { Header = "設定" };
        settingsItem.Click += (s, e) =>
        {
            OpenSettingsWindow();
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // 終了
        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (s, e) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnIMEStateChanged(LanguageInfo languageInfo)
    {
        // IMEMonitor のタイマーコールバック（ThreadPool スレッド）から呼ばれるため
        // Dispatcher.InvokeAsync で UI スレッドにマーシャリング
        Dispatcher.InvokeAsync(() =>
        {
            _mainViewModel?.UpdateIMEState(languageInfo);
            ApplyWindowVisibility(languageInfo);
        });
    }

    private void ApplyWindowVisibility(LanguageInfo languageInfo)
    {
        if (_mouseCursorIndicatorWindow == null || _settingsManager == null) return;
        var settings = _settingsManager.Settings.MouseCursorIndicator;

        // トレイメニューで非表示の場合は無条件非表示
        if (!settings.IsVisible) return;

        bool isJapaneseImeOn = languageInfo.Language == LanguageType.Japanese && languageInfo.IsIMEOn;
        bool shouldShow = !settings.HideWhenImeOff || isJapaneseImeOn;

        if (shouldShow) _mouseCursorIndicatorWindow.Show();
        else _mouseCursorIndicatorWindow.Hide();
    }

    private void OnCursorPositionChanged(int x, int y)
    {
        // MouseTracker はバックグラウンドスレッドから発火するため
        // SetWindowPos (P/Invoke) は任意スレッドから呼べるので直接実行
        if (_mainViewModel != null && _mouseCursorIndicatorWindow != null)
        {
            var vm = _mainViewModel.MouseCursorIndicatorViewModel;
            double posX = x + vm.OffsetX;
            double posY = y + vm.OffsetY;
            _mouseCursorIndicatorWindow.UpdatePosition(posX, posY);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // イベント購読を解除してからDispose
        if (_imeMonitor != null)
        {
            _imeMonitor.IMEStateChanged -= OnIMEStateChanged;
            _imeMonitor.CursorPositionChanged -= OnCursorPositionChanged;
        }

        try { _imeMonitor?.Dispose(); }
        catch (Exception ex) { Trace.TraceError($"[App.OnExit] IMEMonitor Dispose failed: {ex.Message}"); }

        try { _mouseCursorIndicatorWindow?.Close(); }
        catch (Exception ex) { Trace.TraceError($"[App.OnExit] MouseCursorIndicatorWindow Close failed: {ex.Message}"); }

        try { _trayIcon?.Dispose(); }
        catch (Exception ex) { Trace.TraceError($"[App.OnExit] TrayIcon Dispose failed: {ex.Message}"); }

        try { _settingsManager?.Save(); }
        catch (Exception ex) { Trace.TraceError($"[App.OnExit] SettingsManager Save failed: {ex.Message}"); }

        try { PixelIMEDetector.DisposeInstance(); }
        catch (Exception ex) { Trace.TraceError($"[App.OnExit] PixelIMEDetector Dispose failed: {ex.Message}"); }

        base.OnExit(e);
    }
}
