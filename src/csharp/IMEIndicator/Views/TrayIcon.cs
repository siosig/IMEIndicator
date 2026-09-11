// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.ComponentModel;
using IMEIndicator.Models;
using IMEIndicator.Services;

namespace IMEIndicator.Views;

/// <summary>
/// タスクトレイアイコンと右クリックメニュー。<c>NotifyIcon</c> + <c>ContextMenuStrip</c> をラップする
/// 標準コンポーネント版。移植元: <c>src/cpp/views/TrayIcon.h</c> / <c>.cpp</c>
/// （独自メッセージウィンドウ + Shell_NotifyIconW による Win32 実装）。
/// メニュー項目の構成・順序は specs/014-port-to-csharp/contracts/ui-parity-contract.md §1 を正とする。
/// </summary>
/// <remarks>
/// <para>
/// 現行 C++ 版は WM_COMMAND のメニュー ID（<c>TrayIcon::MenuId</c>、1001〜1099 / 5000〜5255）で
/// クリックを判別するが、C# 版では <c>ToolStripMenuItem.Click</c> を直接購読できるため
/// Win32 スタイルの整数コマンド ID は使わない。
/// </para>
/// <para>
/// 注意（既知の不整合）: <c>AppConstants</c> の <c>Tray*</c> 定数（<c>TrayToggleIndicator</c> =
/// 1001 等）は 010-hotkeyp-merge 時代の <c>src/cpp/app/AppConstants.h</c>
/// 「トレイメニュー項目 ID 範囲」をそのまま移植したものだが、現行の
/// <c>src/cpp/views/TrayIcon.h</c> 内部 <c>MenuId</c> 列挙（電源モード 1010〜1012 / 設定 1020 /
/// 終了 1099）とはすでに値が食い違っている。現行 C++ の <c>TrayIcon.cpp</c> 自身も
/// <c>AppConstants::Tray*</c> を一切参照しておらず（実質デッドコード）、電源モード 3 項目・
/// ホットキー動的項目に対応する定数も存在しない。そのため本クラスは <c>AppConstants.Tray*</c> を
/// 使わず、ui-parity-contract.md §1 の表示順序どおりにメニューを直接構築する。
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _toggleIndicatorItem;
    private readonly ToolStripMenuItem _toggleBackgroundImageItem;
    private readonly ToolStripMenuItem _powerBestEfficiencyItem;
    private readonly ToolStripMenuItem _powerBalancedItem;
    private readonly ToolStripMenuItem _powerBestPerformanceItem;
    private readonly ToolStripMenuItem _hotkeyMenuItem;
    private bool _disposed;

    /// <summary>「表示切替」クリック時（カーソル追従インジケーターの表示トグル要求）。</summary>
    public event Action? ToggleIndicatorRequested;

    /// <summary>「背景画像表示切替」クリック時。</summary>
    public event Action? ToggleBackgroundImageRequested;

    /// <summary>電源モードサブメニューのいずれかをクリックしたとき。引数はクリックされたモード。</summary>
    public event Action<PowerMode>? PowerModeSelected;

    /// <summary>「設定」クリック時、および左クリック・左ダブルクリック時。</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>「終了」クリック時。</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// ホットキーサブメニューの動的構築プロバイダ。メニューを開く直前
    /// （<c>ContextMenuStrip.Opening</c>）に呼び出す。<see langword="null"/> または 0 件を返した
    /// 場合は「ホットキー」サブメニュー項目自体を非表示にする（ui-parity-contract.md §1:
    /// trayMenu=true かつ disable=false のエントリのみ、0 件なら項目を出さない）。
    /// </summary>
    public Func<IReadOnlyList<(string Label, Action Run)>>? HotkeyMenuProvider { get; set; }

    public TrayIcon()
    {
        _icon = LoadTrayIcon();

        _toggleIndicatorItem = new ToolStripMenuItem("表示切替");
        _toggleIndicatorItem.Click += (_, _) => ToggleIndicatorRequested?.Invoke();

        _toggleBackgroundImageItem = new ToolStripMenuItem("背景画像表示切替");
        _toggleBackgroundImageItem.Click += (_, _) => ToggleBackgroundImageRequested?.Invoke();

        _powerBestEfficiencyItem = new ToolStripMenuItem("最適な電力効率");
        _powerBestEfficiencyItem.Click += (_, _) => PowerModeSelected?.Invoke(PowerMode.BestPowerEfficiency);

        _powerBalancedItem = new ToolStripMenuItem("バランス");
        _powerBalancedItem.Click += (_, _) => PowerModeSelected?.Invoke(PowerMode.Balanced);

        _powerBestPerformanceItem = new ToolStripMenuItem("最適なパフォーマンス");
        _powerBestPerformanceItem.Click += (_, _) => PowerModeSelected?.Invoke(PowerMode.BestPerformance);

        var powerModeMenuItem = new ToolStripMenuItem("電源モード");
        powerModeMenuItem.DropDownItems.AddRange(
        [
            _powerBestEfficiencyItem,
            _powerBalancedItem,
            _powerBestPerformanceItem,
        ]);

        // ホットキー: 項目自体は常にコレクションへ置き、Opening のたびに再構築して
        // 0 件なら Visible = false にする（項目を都度 Items から抜き差ししない）。
        _hotkeyMenuItem = new ToolStripMenuItem("ホットキー") { Visible = false };

        var openSettingsItem = new ToolStripMenuItem("設定");
        openSettingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.AddRange(
        [
            _toggleIndicatorItem,
            _toggleBackgroundImageItem,
            new ToolStripSeparator(),
            powerModeMenuItem,
            _hotkeyMenuItem,
            new ToolStripSeparator(),
            openSettingsItem,
            new ToolStripSeparator(),
            exitItem,
        ]);
        _contextMenu.Opening += OnContextMenuOpening;

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "IME Indicator",
            ContextMenuStrip = _contextMenu,
            Visible = false,
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
        _notifyIcon.MouseDoubleClick += OnNotifyIconMouseDoubleClick;
    }

    /// <summary>トレイアイコンを表示する。</summary>
    public void Show() => _notifyIcon.Visible = true;

    /// <summary>
    /// メニューのチェック状態を実際のアプリケーション状態へ同期させる。状態が変化しうる
    /// あらゆる経路（トグルクリックの反映後、設定適用後、電源モード変更後）の呼び出し元で
    /// 呼ぶこと。現行 C++ 版は右クリックのたびに HMENU を再構築して常に最新状態を反映するが、
    /// C# 版は ContextMenuStrip を使い回すため、この明示的な同期が必要になる。
    /// </summary>
    public void RefreshChecks(bool indicatorVisible, bool backgroundImageVisible, PowerMode currentPowerMode)
    {
        _toggleIndicatorItem.Checked = indicatorVisible;
        _toggleBackgroundImageItem.Checked = backgroundImageVisible;
        _powerBestEfficiencyItem.Checked = currentPowerMode == PowerMode.BestPowerEfficiency;
        _powerBalancedItem.Checked = currentPowerMode == PowerMode.Balanced;
        _powerBestPerformanceItem.Checked = currentPowerMode == PowerMode.BestPerformance;
    }

    /// <summary>
    /// バルーン通知を表示する（/powertoggle 受信時、電源モード切替時に使用。
    /// ui-parity-contract.md §1）。
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        // Windows Vista 以降、表示時間は OS 側の設定に従うため timeout 引数は実質無視される。
        // 値そのものに意味は無いが API 仕様上必須のため慣例値を渡す。
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e) => RebuildHotkeySubmenu();

    private void RebuildHotkeySubmenu()
    {
        // 直前の Opening で構築した項目を破棄してから作り直す（Click ハンドラのクロージャごと
        // 積み上がらないようにする。ホットキー最大 256 件・開くたびの再構築でも軽量な操作）。
        ToolStripItem[] oldItems = [.. _hotkeyMenuItem.DropDownItems.Cast<ToolStripItem>()];
        _hotkeyMenuItem.DropDownItems.Clear();
        foreach (ToolStripItem oldItem in oldItems)
        {
            oldItem.Dispose();
        }

        IReadOnlyList<(string Label, Action Run)> entries = HotkeyMenuProvider?.Invoke() ?? [];
        if (entries.Count == 0)
        {
            _hotkeyMenuItem.Visible = false;
            return;
        }

        foreach ((string label, Action run) in entries)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => run();
            _hotkeyMenuItem.DropDownItems.Add(item);
        }

        _hotkeyMenuItem.Visible = true;
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenSettingsRequested?.Invoke();
        }
    }

    private void OnNotifyIconMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenSettingsRequested?.Invoke();
        }
    }

    /// <summary>
    /// トレイアイコン画像を読み込む。RELEASE ビルドは Resources/app.ico、DEBUG ビルドは
    /// Resources/app-debug.ico を使う。
    /// </summary>
    /// <remarks>
    /// 現状 .csproj には Resources/*.ico を出力ディレクトリへコピーする設定が無い
    /// （Resources/ime-on-background.png と異なり、これらの .ico は EmbeddedResource 登録も
    /// されておらず、&lt;ApplicationIcon&gt; による実行ファイル自身への埋め込みのみが Release 用に
    /// 存在する）。本タスク（T039）は .csproj 編集がスコープ外のため出力ディレクトリへの配置は
    /// 対応しない。実行時にファイルが見つからない場合は、実行ファイル自身に埋め込まれた既定
    /// アイコン（&lt;ApplicationIcon&gt; = 常に app.ico）へフォールバックし、アイコン読み込み失敗
    /// のみを理由にトレイアイコンの初期化自体が失敗することはない。
    /// </remarks>
    private static Icon LoadTrayIcon()
    {
        string fileName =
#if DEBUG
            "app-debug.ico";
#else
            "app.ico";
#endif
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
        if (File.Exists(path))
        {
            try
            {
                return new Icon(path);
            }
            catch (Exception ex)
            {
                Log.Tray.Warning(ex, "トレイアイコンファイルの読み込みに失敗しました: {Path}", path);
            }
        }

        try
        {
            Icon? fallback = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (fallback is not null)
            {
                return fallback;
            }
        }
        catch (Exception ex)
        {
            Log.Tray.Warning(ex, "実行ファイルからのアイコン抽出に失敗しました: {Path}", Application.ExecutablePath);
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        // NotifyIcon.Dispose() は関連付けた ContextMenuStrip を破棄しないため明示的に破棄する
        // （子の ToolStripMenuItem 群も ToolStrip.Dispose() 経由で連鎖的に破棄される）。
        _contextMenu.Dispose();
        _icon.Dispose();
    }
}
