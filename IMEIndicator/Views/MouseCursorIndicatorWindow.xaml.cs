using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using IMEIndicator.Services;
using IMEIndicator.ViewModels;

namespace IMEIndicator.Views;

public partial class MouseCursorIndicatorWindow : Window
{
    private readonly MouseCursorIndicatorViewModel _viewModel;
    private IntPtr _hwnd;

    public MouseCursorIndicatorWindow(MouseCursorIndicatorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // DPI変更時にもサイズを再設定（マルチモニター環境対応）
        DpiChanged += (_, _) => ApplyWindowSize();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);

        // スタートアップ時の縦長バグ対策：ウィンドウサイズを明示的に設定
        ApplyWindowSize();
    }

    /// <summary>
    /// ウィンドウサイズをDPIスケールを考慮して明示的に設定する
    /// </summary>
    private void ApplyWindowSize()
    {
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int physSize = (int)Math.Round(_viewModel.Size * dpi.DpiScaleX);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, physSize, physSize,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    /// <summary>
    /// ウィンドウ位置を更新する（UIスレッドから呼び出し前提）
    /// </summary>
    public void UpdatePosition(double x, double y)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOSENDCHANGING);
        }
    }
}
