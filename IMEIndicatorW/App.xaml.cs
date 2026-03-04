using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using IMEIndicatorClock.Services;
using IMEIndicatorClock.ViewModels;
using IMEIndicatorClock.Views;

namespace IMEIndicatorClock;

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
        DebugLogService.DebugLevel = -5;
#else
        DebugLogService.DebugLevel = 3;
#endif

        DbgLog.I("アプリケーション起動開始");

        try
        {
            // 設定の読み込み
            DbgLog.I("設定マネージャー作成");
            _settingsManager = new SettingsManager();
            _settingsManager.Load();

#if DEBUG
            DebugLogService.DebugLevel = -5;
#endif

            // ViewModelの初期化
            DbgLog.I("MainViewModel初期化開始");
            _mainViewModel = new MainViewModel(_settingsManager);
            DbgLog.I("MainViewModel初期化完了");

            // IMEモニターの開始
            DbgLog.I("IMEモニター開始");
            _imeMonitor = new IMEMonitor();
            _imeMonitor.PollingInterval = _settingsManager.Settings.Debug.PollingInterval;
            _imeMonitor.IMEStateChanged += OnIMEStateChanged;
            _imeMonitor.CursorPositionChanged += OnCursorPositionChanged;
            _imeMonitor.Start();

            // マウスカーソルインジケーターウィンドウ
            DbgLog.I("MouseCursorIndicatorWindow作成");
            _mouseCursorIndicatorWindow = new MouseCursorIndicatorWindow(_mainViewModel.MouseCursorIndicatorViewModel);
            if (_settingsManager.Settings.MouseCursorIndicator.IsVisible)
            {
                _mouseCursorIndicatorWindow.Show();
            }

            // システムトレイアイコン
            DbgLog.I("システムトレイアイコン初期化");
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

            DbgLog.I("アプリケーション起動完了");
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "アプリケーション起動エラー");
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
        catch
        {
            // 埋め込みリソースから読み込めない場合、外部ファイルを試す
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

#if DEBUG
        // デバッグメニュー
        menu.Items.Add(new System.Windows.Controls.Separator());

        var debugMenu = new System.Windows.Controls.MenuItem { Header = "デバッグ" };

        var openLogItem = new System.Windows.Controls.MenuItem { Header = "ログファイルを開く" };
        openLogItem.Click += (s, e) => DebugLogService.OpenLogFile();
        debugMenu.Items.Add(openLogItem);

        var clearLogItem = new System.Windows.Controls.MenuItem { Header = "ログをクリア" };
        clearLogItem.Click += (s, e) => DebugLogService.ClearLogFile();
        debugMenu.Items.Add(clearLogItem);

        menu.Items.Add(debugMenu);
#endif

        menu.Items.Add(new System.Windows.Controls.Separator());

        // 終了
        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (s, e) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnIMEStateChanged(LanguageInfo languageInfo)
    {
        DbgLog.Log(4, $"[App] OnIMEStateChanged: {languageInfo.Language}/{languageInfo.IsIMEOn}");
        Dispatcher.Invoke(() =>
        {
            _mainViewModel?.UpdateIMEState(languageInfo);
        });
    }

    private void OnCursorPositionChanged(int x, int y)
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainViewModel != null && _mouseCursorIndicatorWindow != null)
            {
                var vm = _mainViewModel.MouseCursorIndicatorViewModel;
                double posX = x + vm.OffsetX;
                double posY = y + vm.OffsetY;
                _mouseCursorIndicatorWindow.UpdatePosition(posX, posY);
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DbgLog.I("アプリケーション終了開始");

        try
        {
            _imeMonitor?.Stop();
            _mouseCursorIndicatorWindow?.Close();
            _trayIcon?.Dispose();
            _settingsManager?.Save();

            DbgLog.I("アプリケーション終了完了");
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "アプリケーション終了エラー");
        }

        base.OnExit(e);
    }
}
