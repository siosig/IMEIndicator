// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.App;

/// <summary>
/// アプリケーション全体で共有する定数。
/// 現行 C++ 実装（src/cpp/app/AppConstants.h）からの 1:1 移植。
/// </summary>
public static class AppConstants
{
    // 単一インスタンス Mutex（specs/014-port-to-csharp/data-model.md §6「名前付きカーネルオブジェクト」）。
    // 現行 C++ 版（AppConstants::MutexName）と同名。移行期間中の共存・相互通知のため。
    public const string SingleInstanceMutexName = "IMEIndicator_SingleInstance";

    // /powertoggle IPC 通知用 Event（specs/014-port-to-csharp/data-model.md §6）。
    // 現行 C++ 版（AppConstants::PowerToggleEventName）と同名。
    public const string PowerToggleEventName = "IMEIndicator_PowerToggle";

    // トレイメニュー項目 ID（src/cpp/app/AppConstants.h と同値。
    // specs/010-hotkeyp-merge/contracts/hotkey-tray-menu-contract.md）。
    public const int TrayToggleIndicator = 1001;
    public const int TrayTogglePixel = 1002;
    public const int TrayOpenSettings = 1003;
    public const int TrayExit = 1004;
    public const int TrayOpenHotkeySettings = 1010; // ホットキー設定タブを開く
    // 013-ime-corner-image: 背景画像表示切替（1002〜1004 は上記の予約済み ID のため 1005）
    public const int TrayToggleBackgroundImage = 1005;
    public const int TrayHotkeyBase = 5000; // 5000 + N で N 番目のホットキーを実行
    public const int TrayHotkeyMax = 5255; // 256 件上限（FR-001）

    // 画面右上 IME ON 背景画像（013-ime-corner-image / spec FR-012）。
    // 論理ピクセル。表示先モニターの DPI で拡縮する（BackgroundImageLayout.Compute）。
    public const int BackgroundImageLogicalSize = 128;
    public const int BackgroundImageLogicalMargin = 16;

    // 背景画像サイズの値域（015-split-appearance-settings FR-001）。
    // AppSettings.Clamp() と SettingsForm の NumericUpDown の双方から参照する。
    public const int BackgroundImageMinSize = 32;
    public const int BackgroundImageMaxSize = 512;

    // 設定ダイアログのタイトル（specs/014-port-to-csharp/contracts/ui-parity-contract.md §4）。
    public const string SettingsTitle = "IME Indicator 設定";
}
