// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using IMEIndicator.App;
using IMEIndicator.Interop;
using IMEIndicator.Models;
using IMEIndicator.Services;

namespace IMEIndicator.Views;

/// <summary>
/// IME ON 時にプライマリモニター作業領域の右上へ、同梱 PNG を最背面・クリック透過で表示する
/// ウィンドウ。表示する/しないの判断は持たない（呼び出し側が <see cref="LayeredWindow.Show"/> /
/// <see cref="LayeredWindow.Hide"/> を呼ぶ）。移植元・正となる契約:
/// specs/013-ime-corner-image/contracts/background-image-window-contract.md、
/// 現行 C++ 版 <c>src/cpp/views/BackgroundImageWindow.h</c> / <c>.cpp</c>。
/// </summary>
/// <remarks>
/// 現行 C++ 版は WIC でデコードするが、C# 版は GDI+（<see cref="Image.FromStream(Stream)"/> +
/// <see cref="InterpolationMode.HighQualityBicubic"/>）で埋め込みリソースから読み込む
/// （「許容する差異」としてライブラリの違いを認めている。リソース名の解決方法は
/// tests/csharp/IMEIndicator.Tests/Services/EmbeddedImageTests.cs の T023 と同じ規則）。
/// </remarks>
public sealed class BackgroundImageWindow : LayeredWindow
{
    private const string ResourceFileName = "ime-on-background.png";

    private Bitmap? _bitmap;
    private Rectangle _lastRect;

    public BackgroundImageWindow()
    {
        // 常に最背面（background-image-window-contract.md §Z 順・入力の保証）。
        BottomMost = true;
    }

    /// <summary>
    /// 最新の配置で表示する（表示前に <see cref="Relayout"/> を行う）。既に表示中なら何もしない。
    /// </summary>
    public override void Show()
    {
        if (IsShown)
        {
            return;
        }
        Relayout();
        base.Show();
        PinToBottom();
    }

    /// <summary>
    /// プライマリモニター・DPI を再取得して表示矩形を再計算する。物理サイズが変わったときだけ
    /// 埋め込み PNG を再デコードして <see cref="LayeredWindow.Present"/> し直し、変わらなければ
    /// 位置だけを安価に反映する（<c>UpdateLayeredWindow</c> の再転送を避ける）。
    /// </summary>
    protected override void Relayout()
    {
        EnsureHandleCreated();

        MonitorInfo? monitor = DisplayHelper.GetPrimaryMonitor();
        if (monitor is null)
        {
            // 契約 §エラーハンドリング: モニター取得失敗時は前回の矩形を維持し警告ログ。
            // 次のトリガー（WM_DISPLAYCHANGE 等）で再試行される。
            Log.Display.Warning("BackgroundImageWindow.Relayout: プライマリモニターの取得に失敗しました。直近の矩形を維持します。");
            return;
        }

        Rectangle rect = BackgroundImageLayout.Compute(
            monitor.WorkRect,
            monitor.DpiX,
            AppConstants.BackgroundImageLogicalSize,
            AppConstants.BackgroundImageLogicalMargin);

        bool sizeChanged = _bitmap is null || _bitmap.Width != rect.Width || _bitmap.Height != rect.Height;
        if (sizeChanged)
        {
            Bitmap? decoded = TryLoadScaledBitmap(rect.Width, rect.Height);
            if (decoded is not null)
            {
                _bitmap?.Dispose();
                _bitmap = decoded;
                Present(decoded, new Point(rect.X, rect.Y), 255);
            }
            // デコード失敗時: 契約どおり既存ビットマップを維持する（無ければ表示しない）。
            // bitmapWidth/Height 相当（_bitmap のサイズ）を更新しないため、次回 Relayout() で再試行される。
        }

        _lastRect = rect;
        // UpdateLayeredWindow は Z 順を変更しないため、Present の有無に関わらず
        // 位置と最背面 Z 順を明示的に確定させる（research.md R-2: 二重に保証する）。
        PinToBottom();
    }

    // 直近の矩形へ位置を合わせ、最背面へ固定する。表示中・非表示中どちらでも安全に呼べる
    // （非表示中に呼んでも見た目には影響しない）。
    private void PinToBottom()
    {
        if (Handle == 0)
        {
            return;
        }
        NativeMethods.SetWindowPos(
            Handle, NativeConstants.HWND_BOTTOM, _lastRect.X, _lastRect.Y, 0, 0,
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_NOSIZE);
    }

    // 埋め込み PNG を本体アセンブリから読み込み、GDI+ の HighQualityBicubic で指定サイズへ縮小する。
    // UpdateLayeredWindow(AC_SRC_ALPHA) は premultiplied alpha を要求するため、Format32bppPArgb で
    // 描画する（LayeredWindow.Present の注意事項と同じ理由。plan.md リスク一覧: 縁が黒ずむ対策）。
    private static Bitmap? TryLoadScaledBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        try
        {
            Assembly assembly = typeof(BackgroundImageWindow).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .SingleOrDefault(n => n.EndsWith(ResourceFileName, StringComparison.Ordinal));
            if (resourceName is null)
            {
                Log.Display.Warning("BackgroundImageWindow: 埋め込みリソース '{ResourceFileName}' が見つかりません。", ResourceFileName);
                return null;
            }

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                Log.Display.Warning("BackgroundImageWindow: 埋め込みリソース '{ResourceName}' のストリームを取得できません。", resourceName);
                return null;
            }

            using Image source = Image.FromStream(stream);

            var scaled = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return scaled;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException
                                        or ExternalException or OutOfMemoryException)
        {
            // GDI+ は不正な画像データも慣例的に OutOfMemoryException で報告するため捕捉に含める
            // （Image.FromStream の既知の挙動）。
            Log.Display.Warning(ex, "BackgroundImageWindow: 埋め込み画像のデコードに失敗しました。");
            return null;
        }
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
