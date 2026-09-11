// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using IMEIndicator.Interop;
using IMEIndicator.Models;
using IMEIndicator.Services;

namespace IMEIndicator.Views;

/// <summary>
/// マウスカーソル追従インジケーター。IME ON 中、カーソル位置 + オフセットへ円形のインジケーター
/// （電源モード色の背景 + 中央の IME ON 文字）を表示する。現行 C++ 版
/// <c>src/cpp/views/MouseCursorIndicatorWindow.cpp</c>（Direct2D + DirectComposition）の色・文字
/// 描画・位置計算を、GDI+ で再現する簡略版（specs/014-port-to-csharp/plan.md Step 6）。
/// 縁の半透明グラデーション・発光・影の質感は「許容する差異」（ui-parity-contract.md）として
/// 単色円 + 単色文字に簡略化する。
/// </summary>
/// <remarks>
/// パフォーマンス方針（マウス移動は高頻度・16ms 間隔で <see cref="MoveTo"/> が呼ばれる想定、
/// T032 MouseTracker 参照）: <see cref="MoveTo"/> は GDI+ 再描画や <c>UpdateLayeredWindow</c> 転送を
/// 伴わない <c>SetWindowPos</c> のみで位置を更新する。ビットマップの再生成・
/// <see cref="LayeredWindow.Present"/>（GDI 転送を伴う）は、設定・電源モード・表示文字が変わった
/// とき（<see cref="ApplySettings"/> / <see cref="SetPowerMode"/> / <see cref="SetText"/>）と
/// <see cref="Relayout"/>（DPI 変化）だけに限定する。
/// </remarks>
public sealed class CursorIndicatorWindow : LayeredWindow
{
    private const string FontFamilyName = "Yu Gothic UI";
    private const float FontSizeRatioToWindow = 0.55f;
    private const string DefaultText = "あ";

    private MouseCursorIndicatorSettings _settings = new();
    private Color _color = ColorHelper.ParseColor(PowerModeColors.GetIndicatorColorHex(PowerMode.Balanced));
    private string _imeOnText = DefaultText;
    private Point _lastCursorPosition = Cursor.Position;
    private Bitmap? _bitmap;

    /// <summary>
    /// 表示設定（サイズ・不透明度・オフセット）を反映して再描画する。
    /// </summary>
    public void ApplySettings(MouseCursorIndicatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Redraw();
    }

    /// <summary>電源モードに応じたインジケーター色（<see cref="PowerModeColors"/>）へ切り替えて再描画する。</summary>
    public void SetPowerMode(PowerMode mode)
    {
        _color = ColorHelper.ParseColor(PowerModeColors.GetIndicatorColorHex(mode));
        Redraw();
    }

    /// <summary>
    /// IME ON 時にインジケーター中央へ描画する文字を設定する。<see langword="null"/> や空文字は
    /// 既定値（"あ"）にフォールバックする（移植元 <c>MouseCursorIndicatorWindow::updateText</c>
    /// と同じ規則）。
    /// </summary>
    public void SetText(string? imeOnText)
    {
        _imeOnText = string.IsNullOrEmpty(imeOnText) ? DefaultText : imeOnText;
        Redraw();
    }

    /// <summary>
    /// カーソル位置に追従して移動する（表示中のみ）。<c>cursor + (offsetX, offsetY) × dpi/96</c> の
    /// 位置へ <c>SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_NOSIZE)</c> するだけで、
    /// GDI+ 再描画・<c>UpdateLayeredWindow</c> 転送は行わない（高頻度呼び出しのため）。
    /// </summary>
    public void MoveTo(Point cursorScreenPosition)
    {
        _lastCursorPosition = cursorScreenPosition;
        if (!IsShown)
        {
            return;
        }

        double dpiScale = GetDpiScale();
        Point target = ComputeScreenPosition(cursorScreenPosition, dpiScale);
        NativeMethods.SetWindowPos(
            Handle, NativeConstants.HWND_TOPMOST, target.X, target.Y, 0, 0,
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_NOSIZE);
    }

    /// <summary>DPI 変化時にビットマップを再生成して再描画する。</summary>
    protected override void Relayout() => Redraw();

    // 自ウィンドウの現在の DPI スケール（dpi/96）。GetDpiForWindow はハンドル作成後のみ有効。
    private double GetDpiScale()
    {
        EnsureHandleCreated();
        uint dpi = NativeMethods.GetDpiForWindow(Handle);
        if (dpi == 0)
        {
            dpi = 96;
        }
        return dpi / 96.0;
    }

    // カーソル座標 + (offsetX, offsetY) × dpiScale の左上座標を返す（T030 契約どおり、
    // インジケーターの半サイズ分の中央合わせは行わない）。
    private Point ComputeScreenPosition(Point cursor, double dpiScale)
    {
        int x = cursor.X + (int)Math.Round(_settings.OffsetX * dpiScale);
        int y = cursor.Y + (int)Math.Round(_settings.OffsetY * dpiScale);
        return new Point(x, y);
    }

    private void Redraw()
    {
        EnsureHandleCreated();
        double dpiScale = GetDpiScale();

        // Size は本来 AppSettings.Clamp() 済みの値が渡ってくる想定だが、
        // 移植元 MouseCursorIndicatorWindow::updateSettings と同様に描画直前でも防御的に丸める。
        double clampedSize = Math.Clamp(_settings.Size, 20.0, 100.0);
        int physicalSize = Math.Max(1, (int)Math.Round(clampedSize * dpiScale));

        Bitmap? bitmap = _bitmap;
        if (bitmap is null || bitmap.Width != physicalSize || bitmap.Height != physicalSize)
        {
            bitmap?.Dispose();
            // UpdateLayeredWindow(AC_SRC_ALPHA) は premultiplied alpha を要求するため PArgb で描画する
            // （LayeredWindow.Present の注意事項と同じ理由。plan.md リスク一覧参照）。
            bitmap = new Bitmap(physicalSize, physicalSize, PixelFormat.Format32bppPArgb);
        }
        _bitmap = bitmap;

        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(_color);
            g.FillEllipse(brush, 0, 0, physicalSize, physicalSize);

            using var font = new Font(FontFamilyName, physicalSize * FontSizeRatioToWindow, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(_imeOnText, font, textBrush, new RectangleF(0, 0, physicalSize, physicalSize), format);
        }

        double clampedOpacity = Math.Clamp(_settings.Opacity, 0.1, 1.0);
        byte alpha = (byte)Math.Clamp((int)Math.Round(clampedOpacity * 255.0), 0, 255);

        Point position = ComputeScreenPosition(_lastCursorPosition, dpiScale);
        Present(bitmap, position, alpha);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }
}
