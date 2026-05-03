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

#include <expected>
#include <nlohmann/json.hpp>
#include <string>
#include <system_error>
#include <windows.h>

namespace imeindicator::models::hotkey {

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

// バリデーションエラー（HotKeyEntry::validate() の失敗種別）
enum class ValidationError : int {
    ExeAndCmdConflict   = 1,  // exe 非空かつ cmd >= 0（排他違反）
    NoTrigger           = 2,  // vkey == 0 かつ cmd < 0（トリガーなし、autoStart も false）
    InvalidCommandId    = 3,  // cmd が 121〜199 / 300〜（予約 or 未定義）
    InvalidCategory     = 4,  // category が [0, 39] 範囲外（クランプで補正される想定だが警告用途）
};

std::error_code make_error_code(ValidationError e) noexcept;

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

    // --- 等値比較（設定の永続化フィールドのみ。実行時状態 processId/process/isDown/lock/item は除外）---
    [[nodiscard]] bool operator==(const HotKeyEntry& o) const noexcept {
        return note == o.note && icon == o.icon && category == o.category
            && exe == o.exe && args == o.args && dir == o.dir
            && sound == o.sound && lirc == o.lirc
            && modifiers == o.modifiers && vkey == o.vkey && scanCode == o.scanCode
            && cmd == o.cmd && cmdShow == o.cmdShow && opacity == o.opacity
            && priority == o.priority
            && disable == o.disable && multInst == o.multInst && trayMenu == o.trayMenu
            && autoStart == o.autoStart && ask == o.ask && delay == o.delay
            && admin == o.admin;
    }

    // --- バリデーション ---

    // 仕様準拠の妥当性検証（contracts/hotkey-entry-schema.md §バリデーション規則）。
    // 戻り値: 成功 = void、失敗 = ValidationError。HotkeyManager::addHotkey 等の登録時に呼ぶ。
    [[nodiscard]] std::expected<void, ValidationError> validate() const noexcept;

    // exe と cmd は排他: exe が空の場合のみ cmd が有効
    // 受け入れる cmd 範囲:
    //   0〜120  : HotkeyP オリジナルコマンド（contracts/internal-command-catalog.md 準拠）
    //   200〜299: IMEIndicator 拡張コマンド（200=ToggleIndicator, 210=PowerModeToggle 等）
    // 121〜199 は未定義範囲のため除外（Phase 2-D 時点で予約）。
    [[nodiscard]] bool isCommand() const noexcept {
        return exe.empty() && cmd >= 0 &&
               (cmd <= 120 || (cmd >= 200 && cmd <= 299));
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

// JSON シリアライザ（contracts/hotkey-entry-schema.md 準拠）
// 実装は HotKeyEntry.cpp。実行時状態（processId/process/isDown/lock/item）は永続化しない。
void to_json(nlohmann::json& j, const HotKeyEntry& e);
void from_json(const nlohmann::json& j, HotKeyEntry& e);

} // namespace imeindicator::models::hotkey

namespace std {
template <>
struct is_error_code_enum<imeindicator::models::hotkey::ValidationError> : true_type {};
}
