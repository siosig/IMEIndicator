using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;
using IMEIndicatorClock.ViewModels;

namespace IMEIndicatorClock.Views;

public partial class ClockWindow : Window
{
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    
    // サイズ制限
    private const double MinSize = 100;
    private const double MaxSize = 500;
    private const int ResizeBorderThickness = 8;

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

    private readonly ClockViewModel _viewModel;
    private HwndSource? _hwndSource;
    private bool _suppressTopmost = false;  // コンテキストメニュー表示中はTOPMOST強制を抑制
    private bool _isResizing = false;  // リサイズ中フラグ（無限ループ防止）
    private bool _isLoaded = false;  // ウィンドウ読み込み完了フラグ
    private System.Windows.Threading.DispatcherTimer? _saveDelayTimer;  // 位置保存用タイマー
    
    // リサイズ用
    private bool _isResizeDragging = false;
    private ResizeDirection _resizeDirection = ResizeDirection.None;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartLeft;
    private double _resizeStartTop;
    
    private enum ResizeDirection
    {
        None,
        Left, Right, Top, Bottom,
        TopLeft, TopRight, BottomLeft, BottomRight
    }

    public ClockWindow(ClockViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // バインディングが正しく動作するよう、明示的にサイズを設定
        Width = viewModel.Width;
        Height = viewModel.Height;

        LocationChanged += OnLocationChanged;
        SizeChanged += OnSizeChanged;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        MouseLeave += OnMouseLeave;
        Loaded += OnLoaded;
        Closed += OnClosed;

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateClockStyle(viewModel.ClockStyle);
        UpdateLayout(viewModel.Layout);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClockViewModel.ClockStyle))
        {
            UpdateClockStyle(_viewModel.ClockStyle);
        }
        else if (e.PropertyName == nameof(ClockViewModel.Layout))
        {
            UpdateLayout(_viewModel.Layout);
        }
    }

    private void UpdateClockStyle(ClockStyle style)
    {
        AnalogClock.Visibility = style == ClockStyle.Analog
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateLayout(DateTimeLayout layout)
    {
        bool dateFirst = layout == DateTimeLayout.VerticalDateFirst ||
                         layout == DateTimeLayout.HorizontalDateFirst;

        var children = TextOverlay.Children;
        if (children.Count >= 2)
        {
            var timeBlock = TimeTextBlock;
            var dateBlock = DateTextBlock;

            children.Clear();

            if (dateFirst)
            {
                children.Add(dateBlock);
                children.Add(timeBlock);
            }
            else
            {
                children.Add(timeBlock);
                children.Add(dateBlock);
            }
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && App.Instance.IsSettingsWindowOpen)
        {
            DragMove();
        }
    }

    private void Border_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
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
        // ウィンドウ読み込み前は無視（初期化時の誤った位置を保存しない）
        if (!_isLoaded) return;

        DbgLog.Log(5, $"ClockWindow.OnLocationChanged: Left={Left}, Top={Top}");
        _viewModel.PositionX = Left;
        _viewModel.PositionY = Top;

        // デバウンス付きで設定を保存（500ms後）
        _saveDelayTimer?.Stop();
        _saveDelayTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveDelayTimer.Tick += (s, args) =>
        {
            _saveDelayTimer?.Stop();
            App.Instance.SettingsManager?.Save();
            DbgLog.Log(4, "ClockWindow: 位置変更を保存");
        };
        _saveDelayTimer.Start();
    }
    
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 無限ループ防止
        if (_isResizing) return;
        
        _isResizing = true;
        try
        {
            // サイズ制限を適用してViewModelを更新
            var newWidth = Math.Clamp(e.NewSize.Width, MinSize, MaxSize);
            var newHeight = Math.Clamp(e.NewSize.Height, MinSize, MaxSize);
            
            if (Math.Abs(_viewModel.Width - newWidth) > 0.5)
            {
                _viewModel.Width = newWidth;
            }
            if (Math.Abs(_viewModel.Height - newHeight) > 0.5)
            {
                _viewModel.Height = newHeight;
            }
        }
        finally
        {
            _isResizing = false;
        }
    }
    
    private ResizeDirection GetResizeDirection(Point point)
    {
        bool left = point.X < ResizeBorderThickness;
        bool right = point.X > ActualWidth - ResizeBorderThickness;
        bool top = point.Y < ResizeBorderThickness;
        bool bottom = point.Y > ActualHeight - ResizeBorderThickness;
        
        if (top && left) return ResizeDirection.TopLeft;
        if (top && right) return ResizeDirection.TopRight;
        if (bottom && left) return ResizeDirection.BottomLeft;
        if (bottom && right) return ResizeDirection.BottomRight;
        if (left) return ResizeDirection.Left;
        if (right) return ResizeDirection.Right;
        if (top) return ResizeDirection.Top;
        if (bottom) return ResizeDirection.Bottom;
        
        return ResizeDirection.None;
    }
    
    private void UpdateCursor(ResizeDirection direction)
    {
        Cursor = direction switch
        {
            ResizeDirection.TopLeft or ResizeDirection.BottomRight => Cursors.SizeNWSE,
            ResizeDirection.TopRight or ResizeDirection.BottomLeft => Cursors.SizeNESW,
            ResizeDirection.Left or ResizeDirection.Right => Cursors.SizeWE,
            ResizeDirection.Top or ResizeDirection.Bottom => Cursors.SizeNS,
            _ => Cursors.Arrow
        };
    }
    
    private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // リサイズドラッグ中
        if (_isResizeDragging)
        {
            var currentPoint = e.GetPosition(this);
            var screenPoint = PointToScreen(currentPoint);
            var deltaX = screenPoint.X - _resizeStartPoint.X;
            var deltaY = screenPoint.Y - _resizeStartPoint.Y;
            
            double newWidth = _resizeStartWidth;
            double newHeight = _resizeStartHeight;
            double newLeft = _resizeStartLeft;
            double newTop = _resizeStartTop;
            
            // 方向に応じてサイズと位置を計算
            switch (_resizeDirection)
            {
                case ResizeDirection.Right:
                    newWidth = _resizeStartWidth + deltaX;
                    break;
                case ResizeDirection.Bottom:
                    newHeight = _resizeStartHeight + deltaY;
                    break;
                case ResizeDirection.Left:
                    newWidth = _resizeStartWidth - deltaX;
                    newLeft = _resizeStartLeft + deltaX;
                    break;
                case ResizeDirection.Top:
                    newHeight = _resizeStartHeight - deltaY;
                    newTop = _resizeStartTop + deltaY;
                    break;
                case ResizeDirection.BottomRight:
                    newWidth = _resizeStartWidth + deltaX;
                    newHeight = _resizeStartHeight + deltaY;
                    break;
                case ResizeDirection.BottomLeft:
                    newWidth = _resizeStartWidth - deltaX;
                    newLeft = _resizeStartLeft + deltaX;
                    newHeight = _resizeStartHeight + deltaY;
                    break;
                case ResizeDirection.TopRight:
                    newWidth = _resizeStartWidth + deltaX;
                    newHeight = _resizeStartHeight - deltaY;
                    newTop = _resizeStartTop + deltaY;
                    break;
                case ResizeDirection.TopLeft:
                    newWidth = _resizeStartWidth - deltaX;
                    newLeft = _resizeStartLeft + deltaX;
                    newHeight = _resizeStartHeight - deltaY;
                    newTop = _resizeStartTop + deltaY;
                    break;
            }
            
            // サイズ制限を適用
            newWidth = Math.Clamp(newWidth, MinSize, MaxSize);
            newHeight = Math.Clamp(newHeight, MinSize, MaxSize);
            
            // 左/上からのリサイズ時、サイズ制限による位置補正
            if (_resizeDirection == ResizeDirection.Left || 
                _resizeDirection == ResizeDirection.TopLeft || 
                _resizeDirection == ResizeDirection.BottomLeft)
            {
                newLeft = _resizeStartLeft + _resizeStartWidth - newWidth;
            }
            if (_resizeDirection == ResizeDirection.Top || 
                _resizeDirection == ResizeDirection.TopLeft || 
                _resizeDirection == ResizeDirection.TopRight)
            {
                newTop = _resizeStartTop + _resizeStartHeight - newHeight;
            }
            
            Width = newWidth;
            Height = newHeight;
            Left = newLeft;
            Top = newTop;
            
            e.Handled = true;
            return;
        }
        
        // 設定ウィンドウが開いている時のみカーソルを変更
        if (!App.Instance.IsSettingsWindowOpen)
        {
            Cursor = Cursors.Arrow;
            return;
        }
        
        var point = e.GetPosition(this);
        var direction = GetResizeDirection(point);
        UpdateCursor(direction);
    }
    
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!App.Instance.IsSettingsWindowOpen) return;
        
        var point = e.GetPosition(this);
        var direction = GetResizeDirection(point);
        
        if (direction != ResizeDirection.None)
        {
            // リサイズ開始
            _isResizeDragging = true;
            _resizeDirection = direction;
            _resizeStartPoint = PointToScreen(point);
            _resizeStartWidth = ActualWidth;
            _resizeStartHeight = ActualHeight;
            _resizeStartLeft = Left;
            _resizeStartTop = Top;
            
            CaptureMouse();
            e.Handled = true;
        }
    }
    
    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizeDragging)
        {
            _isResizeDragging = false;
            _resizeDirection = ResizeDirection.None;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
    
    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Cursor = Cursors.Arrow;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);

        // バインディングが不完全な場合があるので、明示的にサイズと位置を設定
        Width = _viewModel.Width;
        Height = _viewModel.Height;
        Left = _viewModel.PositionX;
        Top = _viewModel.PositionY;

        DbgLog.Log(3, $"ClockWindow OnLoaded: Width={Width}, Height={Height}, Left={Left}, Top={Top}");

        // 位置設定後にフラグを有効化（少し遅延させる）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _isLoaded = true;
            DbgLog.Log(4, "ClockWindow: _isLoaded=true");
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        DbgLog.Log(4, "ClockWindow 閉じました");
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
                    DbgLog.Log(4, $"[TOPMOST] Clock: 抑制中のためスキップ (0x{pos.hwndInsertAfter:X})");
                }
                else
                {
                    DbgLog.Log(4, $"[TOPMOST] Clock: 維持 (0x{pos.hwndInsertAfter:X} → TOPMOST)");
                    pos.hwndInsertAfter = HWND_TOPMOST;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
        }
        return IntPtr.Zero;
    }
}
