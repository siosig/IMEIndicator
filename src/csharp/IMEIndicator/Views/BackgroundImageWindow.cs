// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;
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
    // ウィンドウ内部メッセージ（Ctrl 押下状態の通知用）。WM_APP を基点にする。
    // https://learn.microsoft.com/windows/win32/winmsg/wm-app
    private const int WM_APP_CONTROL_KEY = NativeConstants.WM_APP + 0x10;
    // ウィンドウ内部メッセージ（モーダル表示による無効化からの再有効化予約用、018-draggable-background-image T023）。
    private const int WM_APP_REENABLE = NativeConstants.WM_APP + 0x11;

    // ドラッグの段階（契約「ドラッグの状態機械」）。ShouldBeGrabbable の判定と、
    // BeginPress/ContinueDrag/CommitOrCancelDrag による遷移の両方で使う。
    private enum DragPhase { None, Pressed, Dragging }

    private Bitmap? _bitmap;
    private Rectangle _lastRect;
    // 直近の Relayout・確定・DPI をまたいだ再読込で表示先に決まったモニターの有効 DPI。
    // ContinueDrag() がこれと現在のモニターの DPI を比較し、再読込の要否を判定する（research.md R-6）。
    private uint _renderDpi;
    private string? _loadedImagePath;
    private BackgroundImageSettings _settings = new();

    // Ctrl キーの押下状態。KeyboardHook のコールバックから NotifyControlKey 経由で
    // WM_APP_CONTROL_KEY として届き、WndProc 内で更新する（UI スレッドのみが触る）。
    private bool _controlDown;
    // BeginPress()/ContinueDrag()/CommitOrCancelDrag() が遷移させる（WndProc 内、UI スレッドのみ）。
    private DragPhase _phase = DragPhase.None;
    // 直近に ApplyHitTestMode() で適用した「つかめる」状態のキャッシュ。不要な
    // SetWindowLongPtrW/SetWindowPos 呼び出しを避ける（契約「ヒットテストの切替」）。
    private bool _currentGrabbable;

    // 押下開始時のカーソル位置・表示矩形（物理 px、仮想スクリーン座標）。FollowPointer の基点。
    private Point _dragStartCursor;
    private Rectangle _dragStartRect;
    // 押下開始時とドラッグ中の Relayout 時に取り直すモニター一覧のスナップショット。
    // ContinueDrag() が BackgroundImagePlacement.SelectMonitor で DPI をまたいだかどうかの判定に使う
    // （research.md R-6、契約「押下・追従・確定・中止」の snapshot）。
    private IReadOnlyList<MonitorInfo> _dragMonitorSnapshot = [];
    // ドラッグ中に Relayout() が呼ばれたときの保留フラグ。確定・中止の直後に消化する
    // （契約「Relayout との関係」）。
    private bool _relayoutPending;

    public BackgroundImageWindow()
    {
        // 常に最背面（background-image-window-contract.md §Z 順・入力の保証）。
        BottomMost = true;
    }

    /// <summary>
    /// Ctrl キーの押下状態が変化したときに App から呼ばれる（<c>KeyboardHook.SetControlKeyCallback</c>
    /// 経由）。ウィンドウハンドルが無ければ何もしない。UI スレッドのメッセージキューへ投げるだけで、
    /// 実際の状態更新は <see cref="WndProc"/> が <c>WM_APP_CONTROL_KEY</c> を受けたときに行う
    /// （契約「Ctrl 状態の検知」）。
    /// </summary>
    public void NotifyControlKey(bool isDown)
    {
        if (Handle != 0)
        {
            NativeMethods.PostMessageW(Handle, WM_APP_CONTROL_KEY, (nuint)(isDown ? 1 : 0), 0);
        }
    }

    /// <summary>
    /// Ctrl の解放を取りこぼした形跡を検知したときに発火する（画像の上でポインターを動かした・
    /// 押下した時点で <c>GetAsyncKeyState</c> が非押下を返した場合。契約「取りこぼしへの備え」）。
    /// App はこれを購読して <c>KeyboardHook.Instance.ResetControlKeyState()</c> を呼ぶ。
    /// </summary>
    public event Action? ControlKeyStateStale;

    /// <summary>
    /// ドラッグの確定でドロップ位置が決まったときに発火する（Information ログの後）。App はこれを
    /// 購読して <c>SettingsManager</c> へ保存する（契約「押下・追従・確定・中止」§確定）。
    /// </summary>
    public event Action<BackgroundImagePosition>? PositionCommitted;

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
        ApplyHitTestMode();
    }

    /// <summary>
    /// <c>SW_HIDE</c> で隠す。既に非表示なら何もしない。「ドラッグ」中なら消える直前の位置で確定し、
    /// 「押下」中なら中止してから隠す。非表示になった後は必ずクリック透過へ戻す
    /// （契約「Show / Hide / Dispose」）。
    /// </summary>
    public override void Hide()
    {
        if (_phase != DragPhase.None)
        {
            CommitOrCancelDrag(releaseCapture: true);
        }
        base.Hide();
        ApplyHitTestMode();
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

        if (_phase != DragPhase.None)
        {
            // ドラッグ中は配置計算・再デコードを保留する。構成変更（DPI/ディスプレイ変更）自体は
            // モニタースナップショットへ反映しておき、確定・中止の直後に保留分を実行する
            // （契約「Relayout との関係」）。
            _dragMonitorSnapshot = DisplayHelper.GetMonitors();
            _relayoutPending = true;
            return;
        }

        // Size は本来 AppSettings.Clamp() 済みの値が渡ってくる想定だが、CursorIndicatorWindow.Redraw と
        // 同様に描画直前でも防御的に丸める（015-split-appearance-settings FR-001/FR-010）。
        double clampedSize = Math.Clamp(_settings.Size, AppConstants.BackgroundImageMinSize, AppConstants.BackgroundImageMaxSize);
        int logicalSize = (int)Math.Round(clampedSize);

        var stopwatch = Stopwatch.StartNew();

        // DevicePath の解決（EnumDisplayDevicesW）は、記憶位置があるときだけ行う。未移動のユーザーに
        // 追加の API 呼び出しを発生させないため（018-draggable-background-image research.md R-5）。
        bool includeDevicePath = _settings.Position is not null;
        IReadOnlyList<MonitorInfo> monitors = DisplayHelper.GetMonitors(includeDevicePath);
        BackgroundImagePlacement.PlacementResult? placement = BackgroundImagePlacement.Resolve(
            monitors, _settings.Position, logicalSize, AppConstants.BackgroundImageLogicalMargin);
        if (placement is null)
        {
            // 契約 §エラーハンドリング: モニター取得失敗時は前回の矩形を維持し警告ログ。
            // 次のトリガー（WM_DISPLAYCHANGE 等）で再試行される。
            Log.Display.Warning("BackgroundImageWindow.Relayout: モニター情報の取得に失敗しました。直近の矩形を維持します。");
            return;
        }
        Rectangle rect = placement.Rect;

        // US1 のドラッグ処理で使用予定（DPI をまたいだモニター判定、research.md R-6）。
        _renderDpi = placement.Monitor.DpiX == 0 ? 96 : placement.Monitor.DpiX;

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

        if (placement.UsedPrimaryFallback)
        {
            // 記憶位置のモニターが見つからずプライマリへフォールバックした。position 自体は書き換えない
            // （contracts/background-image-interaction-contract.md「Relayout との関係」節、FR-019）。
            Log.Display.Debug("BackgroundImageWindow.Relayout: 置いたモニターが見つからずプライマリへ表示しています。");
        }
        // 性能確認用（018-draggable-background-image research.md R-11: 移動済み/未移動での所要時間の差を計測する）。
        Log.Display.Debug("BackgroundImageWindow.Relayout: 所要時間 {ElapsedMs} ms", stopwatch.Elapsed.TotalMilliseconds);
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

    // つかめる状態にすべきか。表示中で、かつ Ctrl 押下中かドラッグの最中（phase != None）。
    // Ctrl を離しても「押下」「ドラッグ」の間は true のまま（契約「ヒットテストの切替」、FR-006）。
    private bool ShouldBeGrabbable => IsShown && (_controlDown || _phase != DragPhase.None);

    // クリック透過（WS_EX_TRANSPARENT）の有無を ShouldBeGrabbable に合わせる。WS_EX_LAYERED は
    // 外さない（α=0 の画素はつかめないまま。research.md R-1）。UpdateLayeredWindow は呼ばない
    // （切替で見た目・位置・大きさは変わらない。FR-003）。スタイル変更の反映には SWP_FRAMECHANGED
    // を伴う SetWindowPos が必要（CreateWindowEx の Remarks、Window Features > Layered Windows 参照）。
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowlongptrw
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-createwindowexw
    // https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows
    private void ApplyHitTestMode()
    {
        bool target = ShouldBeGrabbable;
        if (target == _currentGrabbable || Handle == 0)
        {
            return;
        }
        int ex = NativeMethods.GetWindowLongPtrW(Handle, NativeConstants.GWL_EXSTYLE);
        ex = target ? (ex & ~NativeConstants.WS_EX_TRANSPARENT) : (ex | NativeConstants.WS_EX_TRANSPARENT);
        nint result = NativeMethods.SetWindowLongPtrW(Handle, NativeConstants.GWL_EXSTYLE, ex);
        // SetWindowLongPtrW は失敗時に戻り値 0 を返すが、変更前の値がたまたま 0 の場合も戻り値は
        // 0 になりうるため、GetLastError が 0 以外であることも合わせて確認する（契約
        // 「エラーハンドリング」。PowerCommands.TryEnableShutdownPrivilege の AdjustTokenPrivileges
        // と同じ判定形に揃える）。
        if (result == 0 && Marshal.GetLastWin32Error() != 0)
        {
            // 契約: 失敗時は警告ログを出し、_currentGrabbable を更新しない（次の再評価で再試行）。
            Log.Display.Warning("BackgroundImageWindow.ApplyHitTestMode: SetWindowLongPtrW に失敗しました。");
            return;
        }
        NativeMethods.SetWindowPos(
            Handle, 0, 0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER
                | NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_FRAMECHANGED);
        _currentGrabbable = target;
    }

    // Ctrl の解放を取りこぼした（画面ロック中に離した、他の低レベルフックがキーアップを止めた等）
    // ときの共通復帰処理。透過へ戻し、App 側（KeyboardHook.ResetControlKeyState）へ通知する
    // （契約「取りこぼしへの備え」）。
    private void HandleStaleControlKey()
    {
        _controlDown = false;
        ApplyHitTestMode();
        ControlKeyStateStale?.Invoke();
    }

    // 押下の開始（WM_LBUTTONDOWN/WM_LBUTTONDBLCLK、段階「なし」で Ctrl 押下中）。開始点・開始矩形・
    // モニタースナップショットを記録し、マウスキャプチャを取ってヒットテストを再評価する
    // （契約「押下・追従・確定・中止」§押下）。
    private void BeginPress()
    {
        _phase = DragPhase.Pressed;
        NativeMethods.GetCursorPos(out POINT cursor);
        _dragStartCursor = new Point(cursor.X, cursor.Y);
        _dragStartRect = _lastRect;
        _dragMonitorSnapshot = DisplayHelper.GetMonitors();
        NativeMethods.SetCapture(Handle);
        ApplyHitTestMode();
    }

    // 追従（「押下」でしきい値を超えたとき、および「ドラッグ」中のマウス移動ごと）。しきい値未満の
    // 「押下」中は何もしない。DPI の異なるモニターをまたいだら、そのモニター向けの大きさへ再読込
    // して差し替える（契約「押下・追従・確定・中止」§追従、research.md R-6）。
    private void ContinueDrag()
    {
        NativeMethods.GetCursorPos(out POINT cursorPoint);
        var cursor = new Point(cursorPoint.X, cursorPoint.Y);

        if (_phase == DragPhase.Pressed)
        {
            int cxDrag = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXDRAG);
            int cyDrag = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYDRAG);
            if (!BackgroundImagePlacement.ExceedsDragThreshold(_dragStartCursor, cursor, cxDrag, cyDrag))
            {
                return;
            }
            _phase = DragPhase.Dragging;
        }

        Rectangle rect = BackgroundImagePlacement.FollowPointer(_dragStartRect, _dragStartCursor, cursor);

        // 大きさを変えた回（DPI をまたいだ回）だけ Present() が位置・大きさを一度に反映するため、
        // 末尾の移動専用 SetWindowPos は「大きさを変えなかったとき」だけ呼ぶ（契約の追従擬似コード
        // 「if 大きさを変えなかった: SetWindowPos(...)」のとおり。二重の SetWindowPos 呼び出しを避ける）。
        bool resized = false;
        MonitorInfo? monitor = BackgroundImagePlacement.SelectMonitor(_dragMonitorSnapshot, rect);
        if (monitor is not null)
        {
            uint monitorDpi = monitor.DpiX == 0 ? 96 : monitor.DpiX;
            if (monitorDpi != _renderDpi)
            {
                // Relayout() と同じ式で論理サイズを求める（専用フィールドが無いため、ここでも算出する。
                // AppConstants.BackgroundImageMinSize/MaxSize・Math.Clamp/Round は Relayout() と同一）。
                double clampedSize = Math.Clamp(_settings.Size, AppConstants.BackgroundImageMinSize, AppConstants.BackgroundImageMaxSize);
                int logicalSize = (int)Math.Round(clampedSize);
                int newSize = BackgroundImagePlacement.SizeFor(monitor, logicalSize);
                Bitmap? bitmap = BackgroundImageSource.Load(_settings.ImagePath, newSize, newSize);
                if (bitmap is not null)
                {
                    rect = BackgroundImagePlacement.RescaleAroundGrabPoint(rect, cursor, newSize);
                    _bitmap?.Dispose();
                    _bitmap = bitmap;
                    _loadedImagePath = _settings.ImagePath;
                    Present(bitmap, new Point(rect.X, rect.Y), ComputeAlpha());
                    _renderDpi = monitorDpi;
                    // 以後はここを基点に平行移動する（契約「追従」）。
                    _dragStartRect = rect;
                    _dragStartCursor = cursor;
                    resized = true;
                }
                else
                {
                    // 契約 §エラーハンドリング: 再読込に失敗したら警告ログを出し、旧ビットマップ・
                    // 旧サイズのまま移動を続ける。
                    Log.Display.Warning("BackgroundImageWindow: ドラッグ中の再読込に失敗しました。旧いビットマップ・サイズのまま移動を続けます。");
                }
            }
        }

        if (!resized)
        {
            NativeMethods.SetWindowPos(
                Handle, 0, rect.X, rect.Y, 0, 0,
                NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
        }
        _lastRect = rect;
    }

    // 確定（「ドラッグ」で終わったとき）または中止（「押下」のまま終わったとき）。段階を先に None へ
    // 戻してから ReleaseCapture するのは、ReleaseCapture 自体が送る WM_CAPTURECHANGED を「自身の後始末」
    // として無視するため（WM_CAPTURECHANGED 契機のときは releaseCapture: false で呼ばれ、二重解放・
    // 再入を避ける）。確定時だけ BackgroundImagePlacement.Commit で位置を寄せ、PositionCommitted を
    // 発火する（契約「押下・追従・確定・中止」§確定・§中止）。
    private void CommitOrCancelDrag(bool releaseCapture)
    {
        bool wasDragging = _phase == DragPhase.Dragging;
        _phase = DragPhase.None;
        if (releaseCapture)
        {
            NativeMethods.ReleaseCapture();
        }

        if (wasDragging)
        {
            IReadOnlyList<MonitorInfo> monitors = DisplayHelper.GetMonitors(includeDevicePath: true);
            BackgroundImagePlacement.CommitResult? result = BackgroundImagePlacement.Commit(monitors, _lastRect);
            if (result is null)
            {
                // 契約 §エラーハンドリング: モニター一覧が空なら警告ログのみ、通知しない
                // （記憶位置は変わらない）。
                Log.Display.Warning("BackgroundImageWindow: ドロップ確定時にモニター情報が取得できませんでした。位置は記憶されません。");
            }
            else
            {
                if (result.Rect.Width != _lastRect.Width || result.Rect.Height != _lastRect.Height)
                {
                    // 寄せで大きさが変わった（作業領域より大きいドロップ矩形が縮められた等）。
                    // 新しい大きさで再デコードして Present で差し替える
                    // （契約「押下・追従・確定・中止」§確定、research.md R-6）。
                    Bitmap? bitmap = BackgroundImageSource.Load(_settings.ImagePath, result.Rect.Width, result.Rect.Height);
                    if (bitmap is not null)
                    {
                        _bitmap?.Dispose();
                        _bitmap = bitmap;
                        _loadedImagePath = _settings.ImagePath;
                        Present(bitmap, new Point(result.Rect.X, result.Rect.Y), ComputeAlpha());
                    }
                    else
                    {
                        // 契約 §エラーハンドリング: 再読込に失敗したら警告ログを出し、既存のビットマップの
                        // まま位置だけ反映する。
                        Log.Display.Warning("BackgroundImageWindow: 確定時の再読込に失敗しました。既存のビットマップのまま位置だけ反映します。");
                        NativeMethods.SetWindowPos(
                            Handle, 0, result.Rect.X, result.Rect.Y, 0, 0,
                            NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
                    }
                }
                else if (result.Rect != _lastRect)
                {
                    // 大きさは同じで位置だけ変わった。
                    NativeMethods.SetWindowPos(
                        Handle, 0, result.Rect.X, result.Rect.Y, 0, 0,
                        NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
                }
                _lastRect = result.Rect;
                _renderDpi = result.Monitor.DpiX == 0 ? 96 : result.Monitor.DpiX;
                Log.Display.Information(
                    "BackgroundImageWindow: 位置を確定しました。monitorId={MonitorId} anchor={Anchor} offsetX={OffsetX} offsetY={OffsetY}",
                    result.Position.MonitorId, result.Position.Anchor, result.Position.OffsetX, result.Position.OffsetY);
                PositionCommitted?.Invoke(result.Position);
            }
        }

        ApplyHitTestMode();
        if (_relayoutPending)
        {
            _relayoutPending = false;
            Relayout();
        }
    }

    /// <summary>
    /// Ctrl 押下通知・アクティブ化防止・カーソル形状の切替を処理する。それ以外のメッセージは
    /// 基底 <see cref="LayeredWindow.WndProc"/>（<c>WM_WINDOWPOSCHANGING</c> / <c>WM_SIZE</c> /
    /// <c>WM_DPICHANGED</c> / <c>WM_DISPLAYCHANGE</c> / <c>WM_SETTINGCHANGE</c>）へ委譲する
    /// （<see cref="LayeredWindow"/> 自体は変更しない。FR-024）。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_APP_CONTROL_KEY)
        {
            _controlDown = m.WParam != 0;
            ApplyHitTestMode();
            return;
        }
        if (m.Msg == NativeConstants.WM_MOUSEACTIVATE)
        {
            // アクティブ化を抑止する。https://learn.microsoft.com/windows/win32/inputdev/wm-mouseactivate
            m.Result = NativeConstants.MA_NOACTIVATE;
            return;
        }
        if (m.Msg == NativeConstants.WM_SETCURSOR)
        {
            // LOWORD(lParam) はヒットテスト結果。
            // https://learn.microsoft.com/windows/win32/menurc/wm-setcursor
            int hitTest = unchecked((short)(long)m.LParam);
            if (hitTest == NativeConstants.HTCLIENT)
            {
                // 段階が「なし」で Ctrl が離されていれば取りこぼし（契約「取りこぼしへの備え」）。
                // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getasynckeystate
                if (_phase == DragPhase.None && (NativeMethods.GetAsyncKeyState(NativeConstants.VK_CONTROL) & 0x8000) == 0)
                {
                    HandleStaleControlKey();
                    base.WndProc(ref m);
                    return;
                }
                // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setcursor
                NativeMethods.SetCursor(NativeMethods.LoadCursorW(0, (nint)NativeConstants.IDC_SIZEALL));
                m.Result = 1;
                return;
            }
        }
        // WM_LBUTTONDOWN は WH_MOUSE_LL 経由（HookEngine）の値に合わせて uint で定義されているため、
        // m.Msg（int）との比較には明示キャストが要る（他の WM_LBUTTON* は int 定義で不要）。
        if (m.Msg == (int)NativeConstants.WM_LBUTTONDOWN || m.Msg == NativeConstants.WM_LBUTTONDBLCLK)
        {
            if (_phase == DragPhase.None)
            {
                if ((NativeMethods.GetAsyncKeyState(NativeConstants.VK_CONTROL) & 0x8000) == 0)
                {
                    HandleStaleControlKey();
                    base.WndProc(ref m);
                    return;
                }
                BeginPress();
            }
            return;
        }
        if (m.Msg == NativeConstants.WM_MOUSEMOVE)
        {
            if (_phase == DragPhase.None)
            {
                if ((NativeMethods.GetAsyncKeyState(NativeConstants.VK_CONTROL) & 0x8000) == 0)
                {
                    HandleStaleControlKey();
                }
                base.WndProc(ref m);
                return;
            }
            bool leftButtonDown = ((int)m.WParam & NativeConstants.MK_LBUTTON) != 0;
            if (!leftButtonDown)
            {
                CommitOrCancelDrag(releaseCapture: true);
                return;
            }
            ContinueDrag();
            return;
        }
        if (m.Msg == NativeConstants.WM_LBUTTONUP)
        {
            if (_phase != DragPhase.None)
            {
                CommitOrCancelDrag(releaseCapture: true);
                return;
            }
            base.WndProc(ref m);
            return;
        }
        if (m.Msg == NativeConstants.WM_CAPTURECHANGED)
        {
            if (_phase != DragPhase.None && m.LParam != Handle)
            {
                // ReleaseCapture は呼ばない（自身が引き起こす WM_CAPTURECHANGED の再入を避ける。
                // 契約「処理するメッセージ」WM_CAPTURECHANGED 行）。
                CommitOrCancelDrag(releaseCapture: false);
                return;
            }
            base.WndProc(ref m);
            return;
        }
        // 018-draggable-background-image research.md R-9: WinForms の Form.ShowDialog() は、モーダル
        // ループ開始時に同じ UI スレッドの表示中トップレベルウィンドウをすべて EnableWindow(false) で
        // 無効化する（dotnet/winforms の Application.ThreadWindows / DisableWindowsForModalLoop
        // (onlyWinForms: false, ...) で確認）。設定画面を開いている間も背景画像をドラッグできるよう、
        // 無効化を検知したら自分自身へ再有効化を予約する（WM_ENABLE ハンドラ内で直接 EnableWindow を
        // 呼ぶと再入になるため、PostMessageW で非同期にする）。
        if (m.Msg == NativeConstants.WM_ENABLE)
        {
            if (m.WParam == 0)
            {
                NativeMethods.PostMessageW(Handle, WM_APP_REENABLE, 0, 0);
            }
            base.WndProc(ref m);
            return;
        }
        if (m.Msg == WM_APP_REENABLE)
        {
            NativeMethods.EnableWindow(Handle, true);
            return;
        }
        base.WndProc(ref m);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _phase != DragPhase.None)
        {
            // 契約: Dispose では確定も通知もせず中止するだけ（spec Edge Cases「ドラッグ中に終了」）。
            _phase = DragPhase.None;
            NativeMethods.ReleaseCapture();
        }
        if (disposing)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }
}
