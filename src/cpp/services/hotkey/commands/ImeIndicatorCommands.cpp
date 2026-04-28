/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 IMEIndicator 拡張コマンド（cmd 200〜299）の実装。
 Phase 2-D ではスタブ実装（ログ出力 + NotImplemented）。
 Phase 6 (US4) で App 経由で MouseCursorIndicatorWindow / IMEMonitor /
 PowerModeService / ProcessPriorityService と接続して本実装する。
*/
#include "ImeIndicatorCommands.h"

#include "../../../app/AppConstants.h"

#include <spdlog/spdlog.h>

namespace imeindicator::services::hotkey {

namespace {

class ImeIndicatorCmdErrorCategory : public std::error_category {
public:
    const char* name() const noexcept override {
        return "imeindicator.hotkey.imeIndicatorCmd";
    }
    std::string message(int ev) const override {
        switch (static_cast<ImeIndicatorCmdError>(ev)) {
            case ImeIndicatorCmdError::InvalidCommand:
                return "command id out of [200, 299]";
            case ImeIndicatorCmdError::ServiceNotInjected:
                return "required IMEIndicator service is not injected";
            case ImeIndicatorCmdError::ApiCallFailed:
                return "IMEIndicator service call failed";
            case ImeIndicatorCmdError::NotImplemented:
                return "command is a stub (Phase 6 / US4 will implement)";
        }
        return "unknown imeIndicator command error";
    }
};

const ImeIndicatorCmdErrorCategory& imeIndicatorCmdErrorCategory() noexcept {
    static const ImeIndicatorCmdErrorCategory inst;
    return inst;
}

} // namespace

std::error_code make_error_code(ImeIndicatorCmdError e) noexcept {
    return {static_cast<int>(e), imeIndicatorCmdErrorCategory()};
}

const wchar_t* getImeIndicatorCommandName(int cmdId) noexcept {
    switch (cmdId) {
        // インジケーター制御（200〜209）
        case 200: return L"IME インジケーター表示切替";
        case 201: return L"ピクセル検出有効/無効";
        case 202: return L"IME 設定リロード";
        // 電源モード（210〜219）
        case 210: return L"電源モード切替（バックアップ付き）";
        case 211: return L"高パフォーマンス電源プラン適用";
        case 212: return L"電源モードバックアップから復元";
        // プロセス優先度（220〜229）
        case 220: return L"指定プロセス優先度ルールを一時停止";
        case 221: return L"指定プロセス優先度ルールを再開";
        case 222: return L"全プロセス優先度ルール一時停止";
        default:  return L"";
    }
}

std::expected<void, ImeIndicatorCmdError>
executeImeIndicatorCommand(int cmdId, std::wstring_view param) noexcept {
    auto log = spdlog::get(std::string(app::AppConstants::LoggerCommand));

    if (!isImeIndicatorCommandId(cmdId)) {
        if (log) log->warn("imeindicator command id out of range: {}", cmdId);
        return std::unexpected(ImeIndicatorCmdError::InvalidCommand);
    }

    // Phase 2-D ではスタブ。Phase 6 (US4) で各 case に本実装を入れる。
    // 現時点では「呼ばれた」ことをログに残し、NotImplemented を返す。
    if (log) {
        log->info("imeindicator stub command invoked: id={} name={} param=[{} chars]",
                  cmdId,
                  reinterpret_cast<const char*>(getImeIndicatorCommandName(cmdId)),
                  param.size());
    }
    (void)param;  // Phase 6 で使う

    return std::unexpected(ImeIndicatorCmdError::NotImplemented);
}

} // namespace imeindicator::services::hotkey
