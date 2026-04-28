#pragma once
/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - HotKey エントリデータ型
 C++20 スタイル: std::wstring / RAII / enum class
 Windows 11 64-bit 専用
*/

#include <string>
#include <windows.h>

// 仮想キーコード定数（既存コードと互換）
inline constexpr UINT vkMouse  = 512;
inline constexpr UINT vkDelete = 513;
inline constexpr UINT vkLirc   = 514;
inline constexpr UINT vkJoy    = 515;

// スキャンコードマスク（マウスホイール用）
inline constexpr DWORD scanWheelUp    = 0x40000000;
inline constexpr DWORD scanWheelDown  = 0x20000000;
inline constexpr DWORD scanWheelRight = 0x10000000;
inline constexpr DWORD scanWheelLeft  = 0x08000000;

// 内部コマンド ID（0〜119）
enum class Command : int {
    EjectCD          = 0,
    CloseCD          = 1,
    Shutdown         = 2,
    Reboot           = 3,
    Sleep            = 4,
    Logoff           = 5,
    ScreenSaver      = 6,
    MaximizeWindow   = 7,
    MinimizeWindow   = 8,
    CloseWindow      = 9,
    AlwaysOnTop      = 10,
    TerminateProcess = 11,
    ControlPanel     = 12,
    VolumeUp         = 13,
    VolumeDown       = 14,
    WaveVolumeUp     = 15,
    WaveVolumeDown   = 16,
    Mute             = 17,
    DesktopResolution = 18,
    DisplayPowerOff  = 19,
    ProcessPriority  = 20,
    SendWindowCmd    = 21,
    EmptyRecycleBin  = 22,
    RandomWallpaper  = 23,
    DiskFreeSpace    = 24,
    HideWindow       = 25,
    SendKeysToWindow = 26,
    Macro            = 27,
    CenterDesktop    = 28,
    BottomLeft       = 29,
    Bottom           = 30,
    BottomRight      = 31,
    Left             = 32,
    Right            = 33,
    TopLeft          = 34,
    Top              = 35,
    TopRight         = 36,
    MoveMouse        = 37,
    ShowShutdownDlg  = 38,
    PlayCD           = 39,
    CDNextTrack      = 40,
    CDStop           = 41,
    CDPrevTrack      = 42,
    LeftClick        = 43,
    MiddleClick      = 44,
    RightClick       = 45,
    IdlePriority     = 46,
    NormalPriority   = 47,
    HighPriority     = 48,
    RealtimePriority = 49,
    AppsAndFeatures  = 50,
    DisplayProps     = 51,
    InternetOptions  = 52,
    Mouse            = 53,
    Multimedia       = 54,
    PowerOptions     = 55,
    System           = 56,
    DateAndTime      = 57,
    MuteWave         = 58,
    BelowNormal      = 59,
    AboveNormal      = 60,
    MultiCommand     = 61,
    EjectCloseCD     = 62,
    LockComputer     = 63,
    Hibernate        = 64,
    MinimizeOthers   = 65,
    Information      = 66,
    PasteText        = 67,
    NextWallpaper    = 68,
    PrevWallpaper    = 69,
    CommandsList     = 70,
    MoveWindow       = 71,
    ResizeWindow     = 72,
    CmdToActiveWnd   = 73,
    KeysToActiveWnd  = 74,
    Wheel            = 75,
    DoubleClick      = 76,
    Opacity          = 77,
    Volume           = 78,
    FourthButton     = 79,
    FifthButton      = 80,
    ShowDesktop      = 81,
    ChangeWallpaper  = 82,
    PrevTask         = 83,
    NextTask         = 84,
    ShowText         = 85,
    DisableKey       = 86,
    DisableAllHotkeys = 87,
    DisableMouse     = 88,
    DesktopSnapshot  = 89,
    WindowSnapshot   = 90,
    HideTrayIcon     = 91,
    StopService      = 92,
    StartService     = 93,
    MacroToActive    = 94,
    DisableJoystick  = 95,
    DisableRemote    = 96,
    DisableKeyboard  = 97,
    HideIcon         = 98,
    RestoreTrayIcon  = 99,
    CDSpeed          = 100,
    HideApplication  = 101,
    MinAppToTray     = 102,
    Magnifier        = 103,
    ClearRecentDocs  = 104,
    DeleteTempFiles  = 105,
    SaveDesktopIcons = 106,
    RestoreDesktopIcons = 107,
    HorizontalWheel  = 108,
    RemoveDrive      = 109,
    OpacityPlus      = 110,
    OpacityMinus     = 111,
    MaximizeAll      = 112,
    ShowHotkeyP      = 113,
    ReloadHook       = 114,
    MinimizeToTray   = 115,
    // Windows 11 新規コマンド
    VirtualDesktopNext  = 116,
    VirtualDesktopPrev  = 117,
    VirtualDesktopNew   = 118,
    VirtualDesktopClose = 119,
    MediaPlayPause      = 120,
    // 番兵値
    None = -1,
};

// 固定カテゴリ（インデックス 0〜11）+ ユーザー定義（12〜）
enum class Category : int {
    Default  = 0,
    All      = 1,
    Keyboard = 2,
    Mouse    = 3,
    Joystick = 4,
    Remote   = 5,
    Commands = 6,
    Programs = 7,
    Documents = 8,
    WebLinks = 9,
    Autorun  = 10,
    TrayMenu = 11,
    UserDefined = 12,  // 12以降はユーザー定義
};

// ホットキー優先度（0=最低〜5=最高）
enum class ProcessPriorityLevel : int {
    Idle        = 0,
    Normal      = 1,
    High        = 2,
    Realtime    = 3,
    BelowNormal = 4,
    AboveNormal = 5,
};

// ウィンドウ表示状態
enum class WindowShow : int {
    Normal    = 0,
    Maximized = 1,
    Minimized = 2,
};

// ホットキーエントリ（C++20 版）
struct HotKeyEntry {
    // --- 識別・表示 ---
    std::wstring note;      // 説明テキスト（空の場合はファイル名が表示）
    int          icon     = 0;      // アイコンリスト内インデックス
    int          category = 0;      // カテゴリ番号（0=default）
    int          item     = 0;      // リストビュー内表示インデックス

    // --- 実行ターゲット ---
    std::wstring exe;       // 実行ファイルパス / URL / 空文字（内部コマンドの場合）
    std::wstring args;      // コマンドライン引数
    std::wstring dir;       // 作業ディレクトリ
    std::wstring sound;     // 実行時に再生する WAV パス
    std::string  lirc;      // WinLIRC ボタン名（ASCII）

    // --- 入力トリガー ---
    UINT  modifiers = 0;    // MOD_CONTROL | MOD_SHIFT | MOD_ALT | MOD_WIN
    UINT  vkey      = 0;    // 仮想キーコード
                            // 512=マウス / 514=WinLIRC / 515=ジョイスティック
    DWORD scanCode  = 0;    // スキャンコード（マウス/JOY情報を兼用）

    // --- 内部コマンド ---
    int cmd = -1;           // 内部コマンドID（0〜119、exe が空の場合のみ有効、-1=非コマンド）

    // --- 実行オプション ---
    WindowShow          cmdShow  = WindowShow::Normal;
    int                 opacity  = 0;           // 0=設定なし / 1〜255
    ProcessPriorityLevel priority = ProcessPriorityLevel::Normal;

    // --- フラグ ---
    bool disable   = false; // ホットキー無効
    bool multInst  = false; // 複数インスタンス許可
    bool trayMenu  = false; // システムトレイメニューに表示
    bool autoStart = false; // HotkeyP 起動時に自動実行
    bool ask       = false; // 実行前に確認ダイアログ
    bool delay     = false; // 実行前に遅延
    bool admin     = false; // 管理者権限で実行
    bool isDown    = false; // キー押下中フラグ（実行時状態）
    mutable int lock = 0;  // マルチコマンド用ロック（mutable: 並行アクセスのため）

    // --- 実行時状態 ---
    DWORD  processId = 0;       // 実行中プロセス PID（0=未実行）
    HANDLE process   = nullptr; // 実行中プロセスハンドル（nullptr=未実行）

    // --- バリデーション ---

    // exe と cmd は排他: exe が空の場合のみ cmd が有効
    [[nodiscard]] bool isCommand() const noexcept {
        return exe.empty() && cmd >= 0 && cmd <= 119;
    }

    // 表示名を取得（note が空の場合は exe のファイル名部分）
    [[nodiscard]] const std::wstring& displayName() const noexcept {
        return note.empty() ? exe : note;
    }

    // カテゴリ自動判定
    [[nodiscard]] Category autoCategory() const noexcept {
        if (autoStart) return Category::Autorun;
        if (trayMenu)  return Category::TrayMenu;
        if (vkey == vkMouse)  return Category::Mouse;
        if (vkey == vkJoy)    return Category::Joystick;
        if (vkey == vkLirc)   return Category::Remote;
        if (exe.empty())      return Category::Commands;
        // URL判定（簡易）
        if (exe.starts_with(L"http://") || exe.starts_with(L"https://") ||
            exe.starts_with(L"ftp://")  || exe.starts_with(L"shell:"))
            return Category::WebLinks;
        // 実行ファイル判定
        const auto ext = exe.substr(exe.size() > 4 ? exe.size() - 4 : 0);
        if (exe.size() >= 4 && (
            _wcsicmp(ext.c_str(), L".exe") == 0 ||
            _wcsicmp(ext.c_str(), L".com") == 0 ||
            _wcsicmp(ext.c_str(), L".bat") == 0))
            return Category::Programs;
        return Category::Documents;
    }
};
