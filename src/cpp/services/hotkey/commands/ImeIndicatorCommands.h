#pragma once
/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 IMEIndicator 拡張コマンド（cmd 200〜299）。
 HotkeyP 由来の commands/* と並列に位置づけ、IMEIndicator 既存サービス
 （MouseCursorIndicatorWindow, IMEMonitor, PowerModeService, ProcessPriorityService）
 をホットキーから直接トリガーするための薄いラッパー群。

 contracts/internal-command-catalog.md §IMEIndicator 拡張コマンド (200〜299) 準拠。
 Phase 2-D ではスタブ（ログ + 成功）として作成し、Phase 6 (US4) で本実装する。
*/

#include <expected>
#include <functional>
#include <string_view>
#include <system_error>
#include <windows.h>

// 前方宣言（IMEIndicator 既存サービスへのポインタを保持するため）
namespace imeindicator::services {
class IMEMonitor;
class SettingsManager;
class ProcessPriorityMonitor;
}
namespace imeindicator::views {
class MouseCursorIndicatorWindow;
}

namespace imeindicator::services::hotkey {

// IMEIndicator 拡張コマンドのエラー
enum class ImeIndicatorCmdError : int {
    InvalidCommand     = 1,  // cmd ID が IMEIndicator 拡張範囲 [200, 299] にない
    ServiceNotInjected = 2,  // 必要なサービス（MouseCursorIndicatorWindow 等）が未注入
    ApiCallFailed      = 3,  // 既存サービス呼び出しが失敗
    NotImplemented     = 4,  // Phase 2-D ではスタブ（Phase 6 で本実装）
};

std::error_code make_error_code(ImeIndicatorCmdError e) noexcept;

// IMEIndicator 拡張コマンドを実行する。
// cmdId が範囲外なら InvalidCommand、Phase 2-D 時点では NotImplemented を返すケースが多い。
// param: 一部のコマンド（PauseProcessPriorityRule など）でプロセス名等を受ける。
[[nodiscard]] std::expected<void, ImeIndicatorCmdError>
executeImeIndicatorCommand(int cmdId, std::wstring_view param) noexcept;

// コマンド ID の有効性チェック（200〜299 のみ）
[[nodiscard]] constexpr bool isImeIndicatorCommandId(int id) noexcept {
    return id >= 200 && id <= 299;
}

// 表示名取得（UI / ログ用）
[[nodiscard]] const wchar_t* getImeIndicatorCommandName(int cmdId) noexcept;

// === サービス注入 ===
// App::initialize() からこれらの setter を呼ぶことで、ImeIndicatorCommands が
// IMEIndicator 既存サービスを操作可能になる（cmd 200〜299 の本実装）。
// nullptr 注入はサービスを切り離す（テスト用途）。

void setIndicatorWindow(views::MouseCursorIndicatorWindow* window) noexcept;
void setImeMonitor(services::IMEMonitor* monitor) noexcept;
void setSettingsManager(services::SettingsManager* manager) noexcept;
void setProcessPriorityMonitor(services::ProcessPriorityMonitor* monitor) noexcept;

// 電源モードトグルハンドラ（cmd 210 用）。
// 設定された場合、cmd 210 はこのハンドラを呼ぶ（App::togglePowerModeAndNotify と同等の処理：
// バックアップ → モード切替 → インジケーター色更新 → トレイバルーン通知）。
// 未設定なら PowerModeService::toggleMode() のフォールバック（通知・色更新なし）。
using PowerModeToggleHandler = std::function<void()>;
void setPowerModeToggleHandler(PowerModeToggleHandler handler) noexcept;

} // namespace imeindicator::services::hotkey

namespace std {
template <>
struct is_error_code_enum<imeindicator::services::hotkey::ImeIndicatorCmdError> : true_type {};
}
