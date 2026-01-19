using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using IMEIndicatorClock.ViewModels;

namespace IMEIndicatorClock.Views;

public partial class MouseCursorIndicatorWindow : Window
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly MouseCursorIndicatorViewModel _viewModel;
    private IntPtr _hwnd;

    public MouseCursorIndicatorWindow(MouseCursorIndicatorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // PropertyChangedでウィンドウ位置を直接更新
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MouseCursorIndicatorViewModel.PositionX))
        {
            Left = _viewModel.PositionX;
        }
        else if (e.PropertyName == nameof(MouseCursorIndicatorViewModel.PositionY))
        {
            Top = _viewModel.PositionY;
        }
    }

    /// <summary>
    /// 直接位置を更新する（高速更新用）
    /// </summary>
    public void UpdatePosition(double x, double y)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
        }

        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, (int)x, (int)y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            Left = x;
            Top = y;
        }
    }
}
