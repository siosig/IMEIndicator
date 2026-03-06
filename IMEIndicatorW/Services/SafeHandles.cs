using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IMEIndicatorClock.Services;

/// <summary>
/// スクリーンデバイスコンテキスト（GetDC/ReleaseDC）用 SafeHandle
/// </summary>
internal sealed partial class SafeDCHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    private SafeDCHandle() : base(true) { }

    public SafeDCHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => ReleaseDC(IntPtr.Zero, handle) != 0;
}

/// <summary>
/// メモリデバイスコンテキスト（CreateCompatibleDC/DeleteDC）用 SafeHandle
/// </summary>
internal sealed partial class SafeMemDCHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    private SafeMemDCHandle() : base(true) { }

    public SafeMemDCHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => DeleteDC(handle);
}

/// <summary>
/// GDIオブジェクト（Bitmap等、DeleteObject で解放）用 SafeHandle
/// </summary>
internal sealed partial class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    private SafeGdiObjectHandle() : base(true) { }

    public SafeGdiObjectHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => DeleteObject(handle);
}

/// <summary>
/// SetWinEventHook/UnhookWinEvent 用 SafeHandle
/// </summary>
internal sealed partial class WinEventHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    public WinEventHookHandle() : base(true) { }

    public WinEventHookHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => UnhookWinEvent(handle);
}

/// <summary>
/// SetWindowsHookEx/UnhookWindowsHookEx 用 SafeHandle
/// </summary>
internal sealed class SafeHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    public SafeHookHandle() : base(true) { }

    public SafeHookHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => UnhookWindowsHookEx(handle);
}

/// <summary>
/// OpenProcess/CloseHandle 用 SafeHandle
/// </summary>
internal sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private SafeProcessHandle() : base(true) { }

    public SafeProcessHandle(IntPtr handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => CloseHandle(handle);
}
