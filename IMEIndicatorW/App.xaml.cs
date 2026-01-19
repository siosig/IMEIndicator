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
    private IMEIndicatorWindow? _imeIndicatorWindow;
    private ClockWindow? _clockWindow;
    private MouseCursorIndicatorWindow? _mouseCursorIndicatorWindow;
    private SettingsManager? _settingsManager;
    private MainViewModel? _mainViewModel;

    // 外部からアクセス可能なインスタンス
    public static App Instance => (App)Current;
    public IMEIndicatorWindow? IMEIndicatorWindow => _imeIndicatorWindow;
    public ClockWindow? ClockWindow => _clockWindow;
    public MouseCursorIndicatorWindow? MouseCursorIndicatorWindow => _mouseCursorIndicatorWindow;
    public SettingsManager? SettingsManager => _settingsManager;
    public MainViewModel? MainViewModel => _mainViewModel;

    /// <summary>
    /// 設定ウィンドウが開いているかどうか
    /// </summary>
    public bool IsSettingsWindowOpen { get; set; } = false;

    /// <summary>
    /// IMEインジケーターの表示切替
    /// </summary>
    public void SetIMEIndicatorVisible(bool visible)
    {
        if (_imeIndicatorWindow == null || _settingsManager == null) return;
        _settingsManager.Settings.IMEIndicator.IsVisible = visible;
        if (visible) _imeIndicatorWindow.Show();
        else _imeIndicatorWindow.Hide();
    }

    /// <summary>
    /// 時計の表示切替
    /// </summary>
    public void SetClockVisible(bool visible)
    {
        if (_clockWindow == null || _settingsManager == null) return;
        _settingsManager.Settings.Clock.IsVisible = visible;
        if (visible) _clockWindow.Show();
        else _clockWindow.Hide();
    }

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

        // デバッグログの初期化（常にファイル出力を有効にする）
#if DEBUG
        DebugLogService.DebugLevel = -5;  // デバッグビルドではファイル出力も有効
#else
        DebugLogService.DebugLevel = 3;   // リリースビルドではエラーのみ
#endif

        DbgLog.I("アプリケーション起動開始");

        try
        {
            // 設定の読み込み
            DbgLog.I("設定マネージャー作成");
            _settingsManager = new SettingsManager();
            _settingsManager.Load();

            // デバッグビルドではログレベルを強制的に-5に維持
#if DEBUG
            DebugLogService.DebugLevel = -5;
#endif

            // 言語設定の適用
            DbgLog.I("言語設定適用");
            if (!string.IsNullOrEmpty(_settingsManager.Settings.Language))
            {
                LocalizationService.Instance.SetLanguage(_settingsManager.Settings.Language);
            }

            // ViewModelの初期化
            DbgLog.I("MainViewModel初期化開始");
            _mainViewModel = new MainViewModel(_settingsManager);
            DbgLog.I("MainViewModel初期化完了");

            // ピクセル検証間隔の設定
            IMEMonitor.SetPixelVerificationInterval(_settingsManager.Settings.IMEIndicator.PixelVerificationIntervalMs);

            // IMEモニターの開始
            DbgLog.I("IMEモニター開始");
            _imeMonitor = new IMEMonitor();
            _imeMonitor.IMEStateChanged += OnIMEStateChanged;
            _imeMonitor.CursorPositionChanged += OnCursorPositionChanged;
            _imeMonitor.Start();

            // IMEインジケーターウィンドウ
            DbgLog.I("IMEIndicatorWindow作成");
            _imeIndicatorWindow = new IMEIndicatorWindow(_mainViewModel.IMEIndicatorViewModel);
            if (_settingsManager.Settings.IMEIndicator.IsVisible)
            {
                _imeIndicatorWindow.Show();
            }

            // 時計ウィンドウ
            DbgLog.I("ClockWindow作成");
            _clockWindow = new ClockWindow(_mainViewModel.ClockViewModel);
            if (_settingsManager.Settings.Clock.IsVisible)
            {
                _clockWindow.Show();
            }

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

            // 言語変更イベントの購読
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            // 設定ウィンドウの表示
            // - デバッグ時: 毎回開く
            // - リリース時: 初回起動時のみ開く
            DbgLog.I("設定ウィンドウ表示判定");
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
                "IMEIndicatorW - エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // コンテキストメニューを再作成
        if (_trayIcon != null)
        {
            _trayIcon.ContextMenu = CreateContextMenu();
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "IME Indicator Clock",
            ContextMenu = CreateContextMenu()
        };
    }

    private System.Drawing.Icon LoadIcon()
    {
        // Debug/Releaseで異なるアイコンを使用
#if DEBUG
        var iconFileName = "app-debug.ico";
#else
        var iconFileName = "app.ico";
#endif
        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconFileName);
        if (System.IO.File.Exists(iconPath))
        {
            return new System.Drawing.Icon(iconPath);
        }
        // デフォルトアイコン（システムアイコン）
        return System.Drawing.SystemIcons.Application;
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var loc = LocalizationService.Instance;
        var menu = new System.Windows.Controls.ContextMenu();

        // IMEインジケーター表示/非表示
        var imeIndicatorItem = new System.Windows.Controls.MenuItem
        {
            Header = loc.GetString("MenuIMEIndicator"),
            IsCheckable = true,
            IsChecked = _settingsManager?.Settings.IMEIndicator.IsVisible ?? true
        };
        imeIndicatorItem.Click += (s, e) =>
        {
            var item = (System.Windows.Controls.MenuItem)s!;
            if (item.IsChecked)
            {
                _imeIndicatorWindow?.Show();
            }
            else
            {
                _imeIndicatorWindow?.Hide();
            }
            if (_settingsManager != null)
            {
                _settingsManager.Settings.IMEIndicator.IsVisible = item.IsChecked;
                _settingsManager.Save();
            }
        };
        menu.Items.Add(imeIndicatorItem);

        // 時計表示/非表示
        var clockItem = new System.Windows.Controls.MenuItem
        {
            Header = loc.GetString("MenuClock"),
            IsCheckable = true,
            IsChecked = _settingsManager?.Settings.Clock.IsVisible ?? true
        };
        clockItem.Click += (s, e) =>
        {
            var item = (System.Windows.Controls.MenuItem)s!;
            if (item.IsChecked)
            {
                _clockWindow?.Show();
            }
            else
            {
                _clockWindow?.Hide();
            }
            if (_settingsManager != null)
            {
                _settingsManager.Settings.Clock.IsVisible = item.IsChecked;
                _settingsManager.Save();
            }
        };
        menu.Items.Add(clockItem);

        // マウスカーソルインジケーター表示/非表示
        var mouseIndicatorItem = new System.Windows.Controls.MenuItem
        {
            Header = loc.GetString("MenuMouseIndicator"),
            IsCheckable = true,
            IsChecked = _settingsManager?.Settings.MouseCursorIndicator.IsVisible ?? false
        };
        mouseIndicatorItem.Click += (s, e) =>
        {
            var item = (System.Windows.Controls.MenuItem)s!;
            if (item.IsChecked)
            {
                _mouseCursorIndicatorWindow?.Show();
            }
            else
            {
                _mouseCursorIndicatorWindow?.Hide();
            }
            if (_settingsManager != null)
            {
                _settingsManager.Settings.MouseCursorIndicator.IsVisible = item.IsChecked;
                _mainViewModel!.MouseCursorIndicatorViewModel.IsVisible = item.IsChecked;
                _settingsManager.Save();
            }
        };
        menu.Items.Add(mouseIndicatorItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // 設定
        var settingsItem = new System.Windows.Controls.MenuItem { Header = loc.GetString("MenuSettings") };
        settingsItem.Click += (s, e) =>
        {
            var settingsWindow = new SettingsWindow(_mainViewModel!);
            settingsWindow.ShowDialog();
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
        var exitItem = new System.Windows.Controls.MenuItem { Header = loc.GetString("MenuExit") };
        exitItem.Click += (s, e) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnIMEStateChanged(LanguageInfo languageInfo)
    {
        DbgLog.Log(4, $"[App] OnIMEStateChanged: {languageInfo.Language}/{languageInfo.IsIMEOn}");
        Dispatcher.Invoke(() =>
        {
            DbgLog.Log(5, $"[App] UpdateIMEState呼び出し: _mainViewModel={(_mainViewModel != null ? "OK" : "NULL")}");
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
            // IMEモニターを停止
            _imeMonitor?.Stop();

            // ウィンドウを閉じる
            _imeIndicatorWindow?.Close();
            _clockWindow?.Close();
            _mouseCursorIndicatorWindow?.Close();

            // トレイアイコンを解放
            _trayIcon?.Dispose();

            // 設定を保存
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
