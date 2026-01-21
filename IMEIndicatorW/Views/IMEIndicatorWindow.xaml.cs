using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using IMEIndicatorClock.Services;
using IMEIndicatorClock.ViewModels;

namespace IMEIndicatorClock.Views;

public partial class IMEIndicatorWindow : Window
{
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    private readonly IMEIndicatorViewModel _viewModel;
    private HwndSource? _hwndSource;
    private bool _suppressTopmost = false;  // コンテキストメニュー表示中はTOPMOST強制を抑制
    private bool _isLoaded = false;  // ウィンドウ読み込み完了フラグ
    private System.Windows.Threading.DispatcherTimer? _saveDelayTimer;  // 位置保存用タイマー

    public IMEIndicatorWindow(IMEIndicatorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        LocationChanged += OnLocationChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && App.Instance.IsSettingsWindowOpen)
        {
            DragMove();
        }
    }

    private void Grid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!App.Instance.IsSettingsWindowOpen)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        // メニュー表示中はTOPMOST強制を抑制
        _suppressTopmost = true;

        var menu = new System.Windows.Controls.ContextMenu();
        var settingsItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationService.Instance.GetString("MenuSettings")
        };
        settingsItem.Click += (s, e) => App.Instance.OpenSettingsWindow();
        menu.Items.Add(settingsItem);

        menu.Closed += (s, e) => _suppressTopmost = false;

        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        // ウィンドウ読み込み前は無視
        if (!_isLoaded) return;

        _viewModel.PositionX = Left;
        _viewModel.PositionY = Top;

        // デバウンス付きでディスプレイ検出と設定保存（500ms後）
        _saveDelayTimer?.Stop();
        _saveDelayTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveDelayTimer.Tick += (s, args) =>
        {
            _saveDelayTimer?.Stop();
            // ディスプレイインデックスを自動検出・更新
            _viewModel.UpdateDisplayFromPosition();
            App.Instance.SettingsManager?.Save();
            DbgLog.Log(4, "IMEIndicatorWindow: 位置変更を保存");
        };
        _saveDelayTimer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);

        // 位置設定後にフラグを有効化（少し遅延させる）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _isLoaded = true;
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        DbgLog.Log(4, "IMEIndicatorWindow 表示完了");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        DbgLog.Log(4, "IMEIndicatorWindow 閉じました");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);

            // TOPMOSTが外されそうになったら強制的にTOPMOSTを維持
            if (pos.hwndInsertAfter != HWND_TOPMOST)
            {
                if (_suppressTopmost)
                {
                    DbgLog.Log(4, $"[TOPMOST] IMEIndicator: 抑制中のためスキップ (0x{pos.hwndInsertAfter:X})");
                }
                else
                {
                    DbgLog.Log(4, $"[TOPMOST] IMEIndicator: 維持 (0x{pos.hwndInsertAfter:X} → TOPMOST)");
                    pos.hwndInsertAfter = HWND_TOPMOST;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
        }
        return IntPtr.Zero;
    }
}
