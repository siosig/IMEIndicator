// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

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
/// 画像の読込・フォールバック・contain フィット描画は <see cref="Services.BackgroundImageSource"/>
/// （ウィンドウ・ハンドルに依存しない静的サービス）に委譲する。本クラスの責務は、表示矩形
/// （物理サイズ）またはユーザー指定画像パスが変化したときにだけ再読込を発生させる判断
/// （<see cref="Relayout"/> 内 <c>sourceChanged</c>）と、得られたビットマップの提示のみ
/// （specs/016-custom-background-image/contracts/background-image-source-contract.md
/// 「再読込トリガー」節）。現行 C++ 版は WIC でデコードするが、C# 版は GDI+ で読み込む
/// （「許容する差異」としてライブラリの違いを認めている）。
/// </remarks>
public sealed class BackgroundImageWindow : LayeredWindow
{
    private Bitmap? _bitmap;
    private Rectangle _lastRect;
    private string? _loadedImagePath;
    private BackgroundImageSettings _settings = new();

    public BackgroundImageWindow()
    {
        // 常に最背面（background-image-window-contract.md §Z 順・入力の保証）。
        BottomMost = true;
    }

    /// <summary>
    /// 表示設定（サイズ・不透明度）を反映する。表示中であれば即座に再描画する
    /// （015-split-appearance-settings FR-006。CursorIndicatorWindow.ApplySettings と同じ形）。
    /// </summary>
    public void ApplySettings(BackgroundImageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Relayout();
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
    /// 埋め込み PNG を再デコードして <see cref="LayeredWindow.Present"/> し直す（高コストな再デコードを
    /// 避ける）。サイズが変わらず不透明度だけが変わった場合は、既存ビットマップのまま
    /// <see cref="LayeredWindow.Present"/> だけをやり直す。
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

        // Size は本来 AppSettings.Clamp() 済みの値が渡ってくる想定だが、CursorIndicatorWindow.Redraw と
        // 同様に描画直前でも防御的に丸める（015-split-appearance-settings FR-001/FR-010）。
        double clampedSize = Math.Clamp(_settings.Size, AppConstants.BackgroundImageMinSize, AppConstants.BackgroundImageMaxSize);
        int logicalSize = (int)Math.Round(clampedSize);

        Rectangle rect = BackgroundImageLayout.Compute(
            monitor.WorkRect,
            monitor.DpiX,
            logicalSize,
            AppConstants.BackgroundImageLogicalMargin);

        bool sourceChanged = _bitmap is null || _bitmap.Width != rect.Width || _bitmap.Height != rect.Height
            || _loadedImagePath != _settings.ImagePath;
        if (sourceChanged)
        {
            Bitmap? decoded = BackgroundImageSource.Load(_settings.ImagePath, rect.Width, rect.Height);
            if (decoded is not null)
            {
                _bitmap?.Dispose();
                _bitmap = decoded;
                _loadedImagePath = _settings.ImagePath;
                Present(decoded, new Point(rect.X, rect.Y), ComputeAlpha());
            }
            // デコード失敗時: 契約どおり既存ビットマップ・_loadedImagePath を維持する（無ければ表示しない）。
            // 次回 Relayout() で再試行される（research.md R-5、contracts/background-image-source-contract.md「再読込トリガー」）。
        }
        else if (_bitmap is not null)
        {
            // サイズ・画像ソースは変わらず不透明度だけが変わったケース。再デコードせず Present だけやり直す
            // （015-split-appearance-settings research.md R-2 の最適化方針）。
            Present(_bitmap, new Point(rect.X, rect.Y), ComputeAlpha());
        }

        _lastRect = rect;
        // UpdateLayeredWindow は Z 順を変更しないため、Present の有無に関わらず
        // 位置と最背面 Z 順を明示的に確定させる（research.md R-2: 二重に保証する）。
        PinToBottom();
    }

    // 不透明度（0.10〜1.00）を 0〜255 の alpha へ換算する。CursorIndicatorWindow.Redraw と同じ式
    // （015-split-appearance-settings research.md R-2: 同じ数値なら 2 つの表示対象で濃さを一致させる）。
    private byte ComputeAlpha()
    {
        double clampedOpacity = Math.Clamp(_settings.Opacity, 0.1, 1.0);
        return (byte)Math.Clamp((int)Math.Round(clampedOpacity * 255.0), 0, 255);
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
