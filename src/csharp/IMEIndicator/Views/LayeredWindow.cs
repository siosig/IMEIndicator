// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Views;

/// <summary>
/// GDI+ で描いた ARGB <see cref="Bitmap"/> を <c>UpdateLayeredWindow</c> で転送するレイヤード
/// ウィンドウの基底クラス。現行 C++ 版の複数実装（<c>MouseCursorIndicatorWindow</c> の
/// Direct2D + DirectComposition、<c>BackgroundImageWindow</c> の WIC + DIB）を、GDI+ 1 本に
/// 統一した簡略版（specs/014-port-to-csharp/plan.md Step 6、FR-012 シンプルさ最優先）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Form"/> ではなく <see cref="NativeWindow"/> 派生を使う（win32-interop-contract.md /
/// plan.md 注意事項: <c>WS_EX_LAYERED</c> は WinForms の <c>WM_PAINT</c> 描画パイプラインと
/// 相性が悪いため、<c>UpdateLayeredWindow</c> 後は <c>WM_PAINT</c> を使わない）。
/// <c>WS_POPUP</c>（オーナーなし）+ <c>WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW |
/// WS_EX_NOACTIVATE</c> のウィンドウを <see cref="EnsureHandleCreated"/> で作成する。
/// </para>
/// <para>
/// 派生クラス（<see cref="CursorIndicatorWindow"/> / <see cref="BackgroundImageWindow"/>）は
/// <see cref="Present"/> でビットマップを転送し、<see cref="Relayout"/> を実装して
/// DPI・ディスプレイ構成の変化に追従する。
/// </para>
/// </remarks>
public abstract class LayeredWindow : NativeWindow, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// 最背面固定モード。<see langword="true"/> のとき、<see cref="WndProc"/> が
    /// <c>WM_WINDOWPOSCHANGING</c> を受けるたびに、<c>SWP_NOZORDER</c> を伴わない Z 順変更要求を
    /// <c>HWND_BOTTOM</c> へ書き換える（background-image-window-contract.md §Z 順・入力の保証、
    /// research.md R-2 相当）。既定は <see langword="false"/>（<see cref="CursorIndicatorWindow"/>
    /// は最前面が必要なため使わない。<see cref="BackgroundImageWindow"/> がコンストラクタで
    /// <see langword="true"/> にする）。
    /// </summary>
    protected bool BottomMost { get; set; }

    /// <summary>
    /// <see cref="Show"/> / <see cref="Hide"/> で最後に設定された「表示したい」という意図。
    /// 実際の OS 上の可視状態ではなく、アプリの意図を表す。<c>WM_SIZE(SIZE_MINIMIZED)</c> の
    /// 自己復帰を、意図的に隠しているウィンドウへは適用しないための判定に使う。
    /// </summary>
    public bool IsShown { get; private set; }

    /// <summary>
    /// ウィンドウハンドルがまだ無ければ、<c>WS_POPUP</c> + レイヤード拡張スタイルで作成する。
    /// 既に作成済みなら何もしない。初期サイズは 1x1 のプレースホルダで、実サイズは最初の
    /// <see cref="Present"/> が <c>UpdateLayeredWindow</c> 経由で反映する。
    /// </summary>
    protected void EnsureHandleCreated()
    {
        if (Handle != 0)
        {
            return;
        }

        var cp = new CreateParams
        {
            ClassName = null,
            Caption = null,
            Style = NativeConstants.WS_POPUP,
            ExStyle = NativeConstants.WS_EX_LAYERED | NativeConstants.WS_EX_TRANSPARENT
                      | NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE,
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
            Parent = 0,
        };
        CreateHandle(cp);
    }

    /// <summary>
    /// <paramref name="bitmap"/> を <c>UpdateLayeredWindow(ULW_ALPHA)</c> でこのウィンドウへ転送する。
    /// 呼び出し側は <see cref="System.Drawing.Imaging.PixelFormat.Format32bppPArgb"/> で描画すること
    /// （<see cref="Bitmap.GetHbitmap(Color)"/> は premultiplied のまま HBITMAP 化するため、
    /// <c>Format32bppArgb</c>（straight alpha）で描くと縁が黒ずむ。plan.md 注意事項参照）。
    /// GDI リソース（HBITMAP / メモリ DC / 画面 DC）は本メソッド内で必ず解放する。
    /// </summary>
    /// <param name="bitmap">転送するビットマップ。物理ピクセルサイズがそのままウィンドウサイズになる。</param>
    /// <param name="screenPosition">転送先の画面左上座標（物理ピクセル、仮想スクリーン座標）。</param>
    /// <param name="alpha">ウィンドウ全体に掛かる不透明度（<c>BLENDFUNCTION.SourceConstantAlpha</c>、0〜255）。</param>
    protected void Present(Bitmap bitmap, Point screenPosition, byte alpha)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        EnsureHandleCreated();

        Graphics? screenGraphics = null;
        nint hdcScreen = 0;
        nint hdcMem = 0;
        nint hBitmap = 0;
        nint oldBitmap = 0;
        try
        {
            screenGraphics = Graphics.FromHwnd(0);
            hdcScreen = screenGraphics.GetHdc();

            hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
            if (hdcMem == 0)
            {
                return;
            }

            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = NativeMethods.SelectObject(hdcMem, hBitmap);

            var pptDst = new POINT { X = screenPosition.X, Y = screenPosition.Y };
            var pptSrc = new POINT { X = 0, Y = 0 };
            var size = new SIZE { CX = bitmap.Width, CY = bitmap.Height };
            var blend = new BLENDFUNCTION
            {
                BlendOp = NativeConstants.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = NativeConstants.AC_SRC_ALPHA,
            };

            NativeMethods.UpdateLayeredWindow(
                Handle, hdcScreen, ref pptDst, ref size,
                hdcMem, ref pptSrc, 0, ref blend, NativeConstants.ULW_ALPHA);
        }
        finally
        {
            if (hdcMem != 0)
            {
                if (oldBitmap != 0)
                {
                    NativeMethods.SelectObject(hdcMem, oldBitmap);
                }
                NativeMethods.DeleteDC(hdcMem);
            }
            if (hBitmap != 0)
            {
                NativeMethods.DeleteObject(hBitmap);
            }
            if (screenGraphics != null)
            {
                if (hdcScreen != 0)
                {
                    screenGraphics.ReleaseHdc(hdcScreen);
                }
                screenGraphics.Dispose();
            }
        }
    }

    /// <summary>非アクティブのまま表示する（<c>SW_SHOWNOACTIVATE</c>）。既に表示中なら何もしない。</summary>
    public virtual void Show()
    {
        if (IsShown)
        {
            return;
        }
        EnsureHandleCreated();
        IsShown = true;
        NativeMethods.ShowWindow(Handle, NativeConstants.SW_SHOWNOACTIVATE);
    }

    /// <summary><c>SW_HIDE</c> で隠す。既に非表示なら何もしない。</summary>
    public virtual void Hide()
    {
        if (!IsShown)
        {
            return;
        }
        IsShown = false;
        if (Handle != 0)
        {
            NativeMethods.ShowWindow(Handle, NativeConstants.SW_HIDE);
        }
    }

    /// <summary>
    /// DPI・ディスプレイ構成の変化を受けて、派生クラスが表示内容・位置を再計算するためのフック。
    /// <see cref="WndProc"/> が <c>WM_DPICHANGED</c> / <c>WM_DISPLAYCHANGE</c> /
    /// <c>WM_SETTINGCHANGE(SPI_SETWORKAREA)</c> を受けたときに呼ぶ。
    /// </summary>
    protected abstract void Relayout();

    /// <inheritdoc/>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeConstants.WM_WINDOWPOSCHANGING:
                if (BottomMost)
                {
                    ForceBottomMost(ref m);
                }
                return;

            case NativeConstants.WM_SIZE:
                // Win+D 等でシェルに最小化されたときの自己復帰。意図的に隠している場合は行わない。
                if ((int)m.WParam == NativeConstants.SIZE_MINIMIZED && IsShown)
                {
                    NativeMethods.ShowWindow(Handle, NativeConstants.SW_SHOWNOACTIVATE);
                }
                return;

            case NativeConstants.WM_DPICHANGED:
            case NativeConstants.WM_DISPLAYCHANGE:
                Relayout();
                return;

            case NativeConstants.WM_SETTINGCHANGE:
                if ((int)m.WParam == NativeConstants.SPI_SETWORKAREA)
                {
                    Relayout();
                }
                return;
        }

        base.WndProc(ref m);
    }

    // WM_WINDOWPOSCHANGING の WINDOWPOS を書き換え、Z 順変更要求を最背面へ強制する。
    // 呼び出し側が明示的に Z 順維持（SWP_NOZORDER）を要求している場合は何もしない。
    // WINDOWPOS を直接書き換えるのは公式に認められた挙動（WM_WINDOWPOSCHANGING のドキュメント参照）。
    // https://learn.microsoft.com/windows/win32/winmsg/wm-windowposchanging
    private static void ForceBottomMost(ref Message m)
    {
        var pos = Marshal.PtrToStructure<WINDOWPOS>(m.LParam);
        if ((pos.Flags & NativeConstants.SWP_NOZORDER) != 0)
        {
            return;
        }
        pos.HwndInsertAfter = NativeConstants.HWND_BOTTOM;
        Marshal.StructureToPtr(pos, m.LParam, false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>ウィンドウハンドルを破棄する。派生クラスは GDI+ リソース解放後に呼ぶこと。</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }
        if (disposing && Handle != 0)
        {
            DestroyHandle();
        }
        _disposed = true;
    }
}
