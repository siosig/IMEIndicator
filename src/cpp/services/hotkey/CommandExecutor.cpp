/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - コマンドエグゼキュータ実装
 コマンド ID 0〜115 をコマンドクラスへディスパッチ。
 joystick (95) / lirc (96) は対象外（noop として扱う）。
*/

#define NOMINMAX
#include "CommandExecutor.h"
#include "commands/DisplayCommands.h"
#include "commands/MediaCommands.h"
#include "commands/MouseCommands.h"
#include "commands/PowerCommands.h"
#include "commands/ProcessCommands.h"
#include "commands/SystemCommands.h"
#include "commands/TextCommands.h"
#include "commands/VolumeCommands.h"
#include "commands/WindowCommands.h"
#include "platform/VirtualDesktop.h"


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// メインウィンドウハンドル（UI 層から設定される）
static HWND s_mainHwnd = nullptr;

void setMainWindowHandle(HWND hwnd) noexcept {
    s_mainHwnd = hwnd;
}

// --- エラーカテゴリ ---

namespace {

struct ExecuteErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override {
        return "ExecuteError";
    }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<ExecuteError>(ev)) {
        case ExecuteError::InvalidCommand:        return "Invalid command ID";
        case ExecuteError::PlatformNotSupported:  return "Platform not supported";
        case ExecuteError::AccessDenied:          return "Access denied";
        case ExecuteError::ProcessLaunchFailed:   return "Process launch failed";
        case ExecuteError::ApiCallFailed:         return "API call failed";
        }
        return "Unknown ExecuteError";
    }
};

const ExecuteErrorCategory& executeErrorCategory() noexcept {
    static ExecuteErrorCategory cat;
    return cat;
}

// エラー変換ヘルパー
[[nodiscard]] ExecuteError fromPowerError(PowerError e) noexcept {
    switch (e) {
    case PowerError::PrivilegeNotHeld: return ExecuteError::AccessDenied;
    case PowerError::ApiCallFailed:    return ExecuteError::ApiCallFailed;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromWindowError(WindowError e) noexcept {
    switch (e) {
    case WindowError::NoWindow:      return ExecuteError::ApiCallFailed;
    case WindowError::ApiCallFailed: return ExecuteError::ApiCallFailed;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromVolumeError(VolumeError e) noexcept {
    switch (e) {
    case VolumeError::ComNotInitialized: return ExecuteError::ApiCallFailed;
    case VolumeError::DeviceNotFound:    return ExecuteError::ApiCallFailed;
    case VolumeError::ApiCallFailed:     return ExecuteError::ApiCallFailed;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromProcessError(ProcessError e) noexcept {
    switch (e) {
    case ProcessError::AccessDenied:    return ExecuteError::AccessDenied;
    case ProcessError::ProcessNotFound: return ExecuteError::ApiCallFailed;
    case ProcessError::ApiCallFailed:   return ExecuteError::ApiCallFailed;
    case ProcessError::InvalidParam:    return ExecuteError::InvalidCommand;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromMediaError(MediaError e) noexcept {
    switch (e) {
    case MediaError::ApiCallFailed:  return ExecuteError::ApiCallFailed;
    case MediaError::DeviceNotFound: return ExecuteError::ApiCallFailed;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromMouseError(MouseError e) noexcept {
    switch (e) {
    case MouseError::ApiCallFailed: return ExecuteError::ApiCallFailed;
    case MouseError::InvalidParam:  return ExecuteError::InvalidCommand;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromTextError(TextError e) noexcept {
    switch (e) {
    case TextError::EmptyText:       return ExecuteError::InvalidCommand;
    case TextError::ClipboardFailed: return ExecuteError::ApiCallFailed;
    case TextError::ApiCallFailed:   return ExecuteError::ApiCallFailed;
    }
    return ExecuteError::ApiCallFailed;
}

[[nodiscard]] ExecuteError fromSystemError(SystemCmdError e) noexcept {
    switch (e) {
    case SystemCmdError::ApiCallFailed: return ExecuteError::ApiCallFailed;
    case SystemCmdError::InvalidParam:  return ExecuteError::InvalidCommand;
    }
    return ExecuteError::ApiCallFailed;
}

// 結果変換マクロ代替ヘルパー
template<typename T, typename ErrConv>
[[nodiscard]] std::expected<void, ExecuteError>
mapResult(std::expected<T, typename std::remove_reference_t<decltype(std::declval<ErrConv>()(typename std::remove_reference_t<decltype(std::declval<ErrConv>().operator()({}))>::value_type{}))>::value_type>&& r,
          ErrConv conv) {
    if (r) return {};
    return std::unexpected(conv(r.error()));
}

} // anonymous namespace

std::error_code make_error_code(ExecuteError e) {
    return {static_cast<int>(e), executeErrorCategory()};
}

// --- コマンド名テーブル ---

static constexpr const wchar_t* kCommandNames[120] = {
    L"Eject CD",                    // 0
    L"Close CD",                    // 1
    L"Shutdown",                    // 2
    L"Reboot",                      // 3
    L"Sleep",                       // 4
    L"Log off",                     // 5
    L"Screen saver",                // 6
    L"Maximize window",             // 7
    L"Minimize window",             // 8
    L"Close window",                // 9
    L"Always on top",               // 10
    L"Terminate process",           // 11
    L"Control panel",               // 12
    L"Volume up",                   // 13
    L"Volume down",                 // 14
    L"Wave volume up",              // 15
    L"Wave volume down",            // 16
    L"Mute",                        // 17
    L"Rotate display",              // 18
    L"Display power off",           // 19
    L"Process priority",            // 20
    L"Send window command",         // 21
    L"Empty recycle bin",           // 22
    L"Random wallpaper",            // 23
    L"Disk free space",             // 24
    L"Hide window",                 // 25
    L"Send keys to window",         // 26
    L"Macro",                       // 27
    L"Center window",               // 28
    L"Move to bottom-left",         // 29
    L"Move to bottom",              // 30
    L"Move to bottom-right",        // 31
    L"Move to left",                // 32
    L"Move to right",               // 33
    L"Move to top-left",            // 34
    L"Move to top",                 // 35
    L"Move to top-right",           // 36
    L"Move mouse",                  // 37
    L"Shutdown dialog",             // 38
    L"Play/Pause",                  // 39
    L"Next track",                  // 40
    L"Stop",                        // 41
    L"Previous track",              // 42
    L"Left click",                  // 43
    L"Middle click",                // 44
    L"Right click",                 // 45
    L"Idle priority",               // 46
    L"Normal priority",             // 47
    L"High priority",               // 48
    L"Realtime priority",           // 49
    L"Apps & features",             // 50
    L"Display properties",          // 51
    L"Internet options",            // 52
    L"Mouse settings",              // 53
    L"Multimedia settings",         // 54
    L"Power options",               // 55
    L"System",                      // 56
    L"Date and time",               // 57
    L"Mute wave",                   // 58
    L"Below normal priority",       // 59
    L"Above normal priority",       // 60
    L"Multi-command",               // 61
    L"Eject/close CD",              // 62
    L"Lock computer",               // 63
    L"Hibernate",                   // 64
    L"Minimize others",             // 65
    L"Information",                 // 66
    L"Paste text",                  // 67
    L"Next wallpaper",              // 68
    L"Previous wallpaper",          // 69
    L"Commands list",               // 70
    L"Move window",                 // 71
    L"Resize window",               // 72
    L"Command to active window",    // 73
    L"Keys to active window",       // 74
    L"Scroll wheel",                // 75
    L"Double click",                // 76
    L"Window opacity",              // 77
    L"Volume",                      // 78
    L"Fourth button",               // 79
    L"Fifth button",                // 80
    L"Show desktop",                // 81
    L"Change wallpaper",            // 82
    L"Previous task",               // 83
    L"Next task",                   // 84
    L"Show text",                   // 85
    L"Disable key",                 // 86
    L"Disable all hotkeys",         // 87
    L"Disable mouse",               // 88
    L"Desktop snapshot",            // 89
    L"Window snapshot",             // 90
    L"Hide tray icon",              // 91
    L"Stop service",                // 92
    L"Start service",               // 93
    L"Macro to active",             // 94
    L"Disable joystick",            // 95 (excluded)
    L"Disable remote",              // 96 (excluded)
    L"Disable keyboard",            // 97
    L"Hide icon",                   // 98
    L"Restore tray icon",           // 99
    L"CD speed",                    // 100
    L"Hide application",            // 101
    L"Minimize to tray",            // 102
    L"Magnifier",                   // 103
    L"Clear recent docs",           // 104
    L"Delete temp files",           // 105
    L"Save desktop icons",          // 106
    L"Restore desktop icons",       // 107
    L"Horizontal wheel",            // 108
    L"Remove drive",                // 109
    L"Opacity +",                   // 110
    L"Opacity -",                   // 111
    L"Maximize all",                // 112
    L"Show HotkeyP",                // 113
    L"Reload hook",                 // 114
    L"Minimize to tray",            // 115
    L"Virtual desktop next",        // 116
    L"Virtual desktop prev",        // 117
    L"New virtual desktop",         // 118
    L"Close virtual desktop",       // 119
};

[[nodiscard]] const wchar_t* getCommandNameById(int cmdId) noexcept {
    if (cmdId < 0 || cmdId > 119) return nullptr;
    return kCommandNames[cmdId];
}

[[nodiscard]] const wchar_t* getCommandName(Command cmd) noexcept {
    return getCommandNameById(static_cast<int>(cmd));
}

// --- ディスパッチ実装 ---

[[nodiscard]] std::expected<void, ExecuteError>
executeCommandById(int cmdId, std::wstring_view param, const HotKeyEntry* /*hk*/) {
    if (!isValidCommandId(cmdId)) {
        return std::unexpected(ExecuteError::InvalidCommand);
    }

    // ウィンドウスナップ（29〜36）: 共通処理
    if (cmdId >= 29 && cmdId <= 36) {
        // ウィンドウを画面の各領域にスナップ
        HWND hw = GetForegroundWindow();
        if (!hw) return std::unexpected(ExecuteError::ApiCallFailed);

        HMONITOR hmon = MonitorFromWindow(hw, MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi{ sizeof(MONITORINFO) };
        if (!GetMonitorInfoW(hmon, &mi)) {
            return std::unexpected(ExecuteError::ApiCallFailed);
        }

        const int w = mi.rcWork.right  - mi.rcWork.left;
        const int h = mi.rcWork.bottom - mi.rcWork.top;
        const int hw2 = w / 2;
        const int hh2 = h / 2;
        const int L = mi.rcWork.left;
        const int T = mi.rcWork.top;

        int x = L, y = T, sw = w, sh = h;
        switch (cmdId) {
        case 29: x = L;       y = T+hh2; sw = hw2; sh = hh2; break; // bottom-left
        case 30: x = L;       y = T+hh2; sw = w;   sh = hh2; break; // bottom
        case 31: x = L+hw2;   y = T+hh2; sw = hw2; sh = hh2; break; // bottom-right
        case 32: x = L;       y = T;     sw = hw2; sh = h;   break; // left
        case 33: x = L+hw2;   y = T;     sw = hw2; sh = h;   break; // right
        case 34: x = L;       y = T;     sw = hw2; sh = hh2; break; // top-left
        case 35: x = L;       y = T;     sw = w;   sh = hh2; break; // top
        case 36: x = L+hw2;   y = T;     sw = hw2; sh = hh2; break; // top-right
        default: break;
        }
        SetWindowPos(hw, nullptr, x, y, sw, sh, SWP_NOZORDER);
        return {};
    }

    // 音量コマンド
    constexpr float kVolumeStep = 0.05f; // 5%
    switch (cmdId) {
    case 13: // VolumeUp
    {
        auto r = adjustVolume(kVolumeStep);
        if (!r) return std::unexpected(fromVolumeError(r.error()));
        return {};
    }
    case 14: // VolumeDown
    {
        auto r = adjustVolume(-kVolumeStep);
        if (!r) return std::unexpected(fromVolumeError(r.error()));
        return {};
    }
    case 15: // WaveVolumeUp
    {
        auto r = adjustVolume(kVolumeStep);
        if (!r) return std::unexpected(fromVolumeError(r.error()));
        return {};
    }
    case 16: // WaveVolumeDown
    {
        auto r = adjustVolume(-kVolumeStep);
        if (!r) return std::unexpected(fromVolumeError(r.error()));
        return {};
    }
    case 17: // Mute
    case 58: // MuteWave
    {
        auto r = toggleMute();
        if (!r) return std::unexpected(fromVolumeError(r.error()));
        return {};
    }
    case 78: // Volume (パラメータで絶対値または相対値を指定)
    {
        auto r = executeVolumeCommand(param);
        if (!r) return std::unexpected(fromVolumeError(r.error()));
        return {};
    }
    default: break;
    }

    // 電源コマンド
    {
        std::expected<void, PowerError> pr = std::unexpected(PowerError::ApiCallFailed);
        bool isPower = true;
        switch (cmdId) {
        case 2:  pr = shutdownSystem();   break;
        case 3:  pr = rebootSystem();     break;
        case 4:  pr = sleepSystem();      break;
        case 5:  pr = logoffUser();       break;
        case 19: pr = turnOffMonitor();   break;
        case 38: pr = shutdownSystem();   break; // ShowShutdownDlg → 即シャットダウン
        case 63: pr = lockWorkstation();  break;
        case 64: pr = hibernateSystem();  break;
        default: isPower = false;         break;
        }
        if (isPower) {
            if (!pr) return std::unexpected(fromPowerError(pr.error()));
            return {};
        }
    }

    // ウィンドウコマンド
    {
        std::expected<void, WindowError> wr = std::unexpected(WindowError::ApiCallFailed);
        bool isWindow = true;
        switch (cmdId) {
        case 7:   wr = maximizeActiveWindow();         break;
        case 8:   wr = minimizeActiveWindow();         break;
        case 9:   wr = closeActiveWindow();            break;
        case 10:  wr = toggleAlwaysOnTop();            break;
        case 25:  wr = toggleAlwaysOnTop();            break; // HideWindow → AlwaysOnTop で代用
        case 28:  wr = centerWindow();                 break;
        case 65:  wr = minimizeToTray(s_mainHwnd);     break;
        case 77:  {
            // Opacity: param を整数として解釈
            int op = 128;
            if (!param.empty()) {
                try { op = std::stoi(std::wstring(param)); }
                catch (...) {}
            }
            wr = setWindowOpacity(op);
            break;
        }
        case 81:  wr = showDesktop();                  break;
        case 83:  wr = switchToNextWindow();            break; // PrevTask
        case 84:  wr = switchToNextWindow();            break; // NextTask
        case 90:  wr = centerWindow();                  break; // WindowSnapshot → center で代用
        case 101: wr = minimizeActiveWindow();          break; // HideApplication
        case 102: wr = minimizeToTray(s_mainHwnd);      break;
        case 110: {
            // OpacityPlus
            HWND hw = GetForegroundWindow();
            if (hw) {
                BYTE alpha = 255;
                DWORD flags = 0;
                GetLayeredWindowAttributes(hw, nullptr, &alpha, &flags);
                wr = setWindowOpacity(std::max(0, static_cast<int>(alpha) - 10));
            } else {
                wr = std::unexpected(WindowError::NoWindow);
            }
            break;
        }
        case 111: {
            // OpacityMinus
            HWND hw = GetForegroundWindow();
            if (hw) {
                BYTE alpha = 255;
                DWORD flags = 0;
                GetLayeredWindowAttributes(hw, nullptr, &alpha, &flags);
                wr = setWindowOpacity(std::min(255, static_cast<int>(alpha) + 10));
            } else {
                wr = std::unexpected(WindowError::NoWindow);
            }
            break;
        }
        case 112: {
            // MaximizeAll: すべてのトップレベルウィンドウを最大化
            EnumWindows([](HWND hw, LPARAM) -> BOOL {
                if (IsWindowVisible(hw) && !IsIconic(hw)) {
                    ShowWindow(hw, SW_MAXIMIZE);
                }
                return TRUE;
            }, 0);
            wr = {};
            break;
        }
        case 115: wr = minimizeToTray(s_mainHwnd);     break;
        default: isWindow = false;                     break;
        }
        if (isWindow) {
            if (!wr) return std::unexpected(fromWindowError(wr.error()));
            return {};
        }
    }

    // マウスコマンド
    switch (cmdId) {
    case 37:
    case 43: case 44: case 45:
    case 75: case 76:
    case 79: case 80:
    case 108: {
        auto r = executeMouseCommand(cmdId);
        if (!r) return std::unexpected(fromMouseError(r.error()));
        return {};
    }
    default: break;
    }

    // メディアコマンド
    switch (cmdId) {
    case 0: case 1:
    case 39: case 40: case 41: case 42:
    case 62: case 100: {
        auto r = executeMediaCommand(cmdId, param);
        if (!r) return std::unexpected(fromMediaError(r.error()));
        return {};
    }
    default: break;
    }

    // テキスト/マクロコマンド
    switch (cmdId) {
    case 27: case 67: case 94: {
        auto r = executeTextCommand(cmdId, param);
        if (!r) return std::unexpected(fromTextError(r.error()));
        return {};
    }
    case 74: // KeysToActiveWnd
    case 26: // SendKeysToWindow
    {
        auto r = executeMacro(param);
        if (!r) return std::unexpected(fromTextError(r.error()));
        return {};
    }
    default: break;
    }

    // プロセスコマンド
    switch (cmdId) {
    case 11: case 20:
    case 46: case 47: case 48: case 49:
    case 59: case 60:
    case 92: case 93: {
        auto r = executeProcessCommand(cmdId, param);
        if (!r) return std::unexpected(fromProcessError(r.error()));
        return {};
    }
    default: break;
    }

    // ディスプレイコマンド
    switch (cmdId) {
    case 18: case 19: case 23: {
        auto r = executeDisplayCommand(cmdId);
        if (!r) return std::unexpected(ExecuteError::ApiCallFailed);
        return {};
    }
    default: break;
    }

    // システムコマンド
    switch (cmdId) {
    case 6:  // ScreenSaver
    case 12: // ControlPanel
    case 22: // EmptyRecycleBin → systemCommand
    case 24: // DiskFreeSpace → openUrl で代用
    case 50: case 51: case 52: case 53: case 54: case 55: case 56: case 57:
    case 61: // MultiCommand
    case 66: // Information
    case 70: // CommandsList
    case 85: case 86: case 87: case 88:
    case 89: // DesktopSnapshot → captureScreen
    case 91: // HideTrayIcon
    case 95: // DisableJoystick (noop)
    case 96: // DisableRemote (noop)
    case 97: // DisableKeyboard
    case 98: // HideIcon
    case 99: // RestoreTrayIcon
    case 103: case 104: case 105: case 106: case 107:
    case 109: case 113: case 114: {
        auto r = executeSystemCommand(cmdId, param);
        if (!r) return std::unexpected(fromSystemError(r.error()));
        return {};
    }
    default: break;
    }

    // Windows 11 仮想デスクトップ (116-119)
    if (cmdId >= 116 && cmdId <= 119) {
        const auto cmd = static_cast<VirtualDesktopCommand>(cmdId);
        auto r = VirtualDesktopManager::execute(cmd);
        if (!r) {
            switch (r.error()) {
            case VirtualDesktopError::NotSupported:
                return std::unexpected(ExecuteError::PlatformNotSupported);
            default:
                return std::unexpected(ExecuteError::ApiCallFailed);
            }
        }
        return {};
    }

    return std::unexpected(ExecuteError::ApiCallFailed);
}

[[nodiscard]] std::expected<void, ExecuteError>
executeCommand(Command cmd, std::wstring_view param, const HotKeyEntry* hk) {
    if (cmd == Command::None) {
        return std::unexpected(ExecuteError::InvalidCommand);
    }
    return executeCommandById(static_cast<int>(cmd), param, hk);
}

} // namespace imeindicator::services::hotkey
