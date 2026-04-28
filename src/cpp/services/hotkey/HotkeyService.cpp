/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
#include "HotkeyService.h"

#include "CommandExecutor.h"
#include "InputRouter.h"
#include "../../app/AppConstants.h"

#include <spdlog/spdlog.h>

namespace imeindicator::services::hotkey {

namespace {

auto hotkeyLog() {
    return spdlog::get(std::string(::imeindicator::app::AppConstants::LoggerHotkey));
}

} // namespace

HotkeyService::HotkeyService() = default;

HotkeyService::~HotkeyService() {
    stop();
}

bool HotkeyService::start(HWND mainHwnd,
                          const models::hotkey::HotkeySettings& settings,
                          models::hotkey::HookMode mode)
{
    auto log = hotkeyLog();
    if (running_) {
        if (log) log->warn("HotkeyService::start called while already running");
        return true;
    }

    if (mainHwnd == nullptr) {
        if (log) log->error("HotkeyService::start: mainHwnd is null");
        return false;
    }

    mainHwnd_    = mainHwnd;
    currentMode_ = mode;

    // CommandExecutor にメインウィンドウハンドルを渡す（WindowCommands::executeWindowCommand 等で利用）
    setMainWindowHandle(mainHwnd_);

    // HotkeyManager に永続化済みエントリをロード
    manager_.loadFromHotkeySettings(settings.hotkeys);

    // HookEngine 起動。コールバックは onHotkeyDetected に転送。
    auto cb = [this](UINT vkey, DWORD scanCode, UINT modifiers) {
        onHotkeyDetected(vkey, scanCode, modifiers);
    };
    auto result = engine_.start(mainHwnd_, currentMode_, cb);
    if (!result) {
        if (log) log->error("HookEngine::start failed: {}",
                            make_error_code(result.error()).message());
        mainHwnd_ = nullptr;
        return false;
    }

    running_ = true;
    if (log) log->info("HotkeyService started: hotkeys={}, mode={}",
                       manager_.count(),
                       static_cast<int>(currentMode_));

    // autoStart=true のエントリを実行（FR-018）
    executeAutoStartEntries();
    return true;
}

void HotkeyService::stop() noexcept {
    if (!running_) return;
    auto log = hotkeyLog();
    engine_.stop();
    manager_.clear();
    setMainWindowHandle(nullptr);
    running_  = false;
    mainHwnd_ = nullptr;
    if (log) log->info("HotkeyService stopped");
}

bool HotkeyService::reload(const models::hotkey::HotkeySettings& settings) {
    auto log = hotkeyLog();
    if (!running_) {
        if (log) log->warn("HotkeyService::reload called while not running");
        return false;
    }

    // HookEngine を一旦停止 → HotkeyManager 更新 → HookEngine 再起動
    HWND savedHwnd  = mainHwnd_;
    auto savedMode  = currentMode_;
    engine_.stop();
    manager_.loadFromHotkeySettings(settings.hotkeys);
    auto cb = [this](UINT vkey, DWORD scanCode, UINT modifiers) {
        onHotkeyDetected(vkey, scanCode, modifiers);
    };
    auto result = engine_.start(savedHwnd, savedMode, cb);
    if (!result) {
        if (log) log->error("HotkeyService::reload: HookEngine restart failed: {}",
                            make_error_code(result.error()).message());
        running_ = false;
        return false;
    }

    if (log) log->info("HotkeyService reloaded: hotkeys={}", manager_.count());
    return true;
}

bool HotkeyService::handleRawHookMessage(UINT msg, WPARAM wParam, LPARAM lParam) {
    if (!running_) return false;
    auto handler = [this](UINT vkey, DWORD scanCode, UINT modifiers) {
        onHotkeyDetected(vkey, scanCode, modifiers);
    };
    return InputRouter::handleRawHookMessage(msg, wParam, lParam, handler);
}

void HotkeyService::onHotkeyDetected(UINT vkey, DWORD scanCode, UINT modifiers) {
    auto log = hotkeyLog();

    // 該当エントリを検索
    const auto* entry = manager_.findHotkeyByKey(vkey, modifiers);
    if (!entry) {
        // 該当なし: ログ出力（debug レベル、無音にしない）
        if (log) log->debug("hotkey detected but no matching entry: vk={:#x} mods={:#x} scan={:#x}",
                            vkey, modifiers, scanCode);
        return;
    }

    if (entry->disable) {
        if (log) log->debug("hotkey is disabled, skipping: note={}",
                            reinterpret_cast<const char*>(entry->displayName().c_str()));
        return;
    }

    // 内部コマンド or 起動アクションのいずれかを実行
    if (entry->isCommand()) {
        // 内部コマンド (cmd 0〜120 / 200〜299)
        auto result = executeCommandById(entry->cmd, entry->args, entry);
        if (!result) {
            if (log) log->warn("executeCommandById failed: cmd={} err={}",
                               entry->cmd,
                               make_error_code(result.error()).message());
        }
    } else if (!entry->exe.empty()) {
        // exe / URL / フォルダ起動 → ProcessCommands::launchApp（Phase 3 で本実装、ここでは
        // CommandExecutor 経由で内部的に分岐するため、cmdId として擬似値を使う方式は採らず、
        // 直接 launchApp 相当を呼ぶ。Phase 2-D 範囲では最小限のログ出力に留める）。
        if (log) log->info("launch action requested: exe={} (full launch impl in Phase 3 / US1)",
                           reinterpret_cast<const char*>(entry->exe.c_str()));
        // TODO(Phase 3 / US1): ProcessCommands::launchApp を呼び出す配線
    }
}

void HotkeyService::executeAutoStartEntries() {
    auto log = hotkeyLog();
    int executed = 0;
    for (const auto& hk : manager_.hotkeys()) {
        if (!hk.autoStart || hk.disable) continue;
        if (hk.isCommand()) {
            auto result = executeCommandById(hk.cmd, hk.args, &hk);
            if (!result && log) {
                log->warn("autoStart command failed: cmd={} err={}",
                          hk.cmd, make_error_code(result.error()).message());
            }
        }
        // exe 起動の autoStart は Phase 3 / US1 で対応
        ++executed;
    }
    if (log && executed > 0) log->info("executed {} autoStart entries", executed);
}

} // namespace imeindicator::services::hotkey
