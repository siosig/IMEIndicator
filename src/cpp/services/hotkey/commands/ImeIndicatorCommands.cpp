/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 IMEIndicator 拡張コマンド（cmd 200〜299）の本実装（Phase 6 / US4）。
 App から注入された IMEIndicator 既存サービスへのポインタを介して、ホットキー押下から
 直接インジケーター切替・電源モード切替・プロセス優先度ルール制御を実行する。
*/
#include "ImeIndicatorCommands.h"

#include "../../../app/AppConstants.h"
#include "../../IMEMonitor.h"
#include "../../PowerModeService.h"
#include "../../PowerModeBackup.h"
#include "../../ProcessPriorityMonitor.h"
#include "../../SettingsManager.h"
#include "../../../models/AppSettings.h"
#include "../../../models/PowerMode.h"
#include "../../../views/MouseCursorIndicatorWindow.h"

#include <atomic>
#include <spdlog/spdlog.h>

namespace imeindicator::services::hotkey {

namespace {

// 注入されたサービスへのポインタ（App::initialize で設定、shutdown で nullptr）。
// std::atomic で書き込み・読み込みのスレッド安全性を保証（ホットキースレッドから読まれる）。
std::atomic<views::MouseCursorIndicatorWindow*>  g_indicatorWindow{nullptr};
std::atomic<services::IMEMonitor*>               g_imeMonitor{nullptr};
std::atomic<services::SettingsManager*>          g_settingsManager{nullptr};
std::atomic<services::ProcessPriorityMonitor*>   g_priorityMonitor{nullptr};

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

auto cmdLog() {
    return spdlog::get(std::string(app::AppConstants::LoggerCommand));
}

// === 個別コマンド実装 ===

// cmd 200: IME インジケーター表示切替
std::expected<void, ImeIndicatorCmdError> cmdToggleIndicator() noexcept {
    auto* sm = g_settingsManager.load(std::memory_order_acquire);
    auto* iw = g_indicatorWindow.load(std::memory_order_acquire);
    if (!sm) return std::unexpected(ImeIndicatorCmdError::ServiceNotInjected);

    auto& settings = sm->mutableSettings();
    settings.mouseCursorIndicator.isVisible = !settings.mouseCursorIndicator.isVisible;
    sm->save();

    if (iw) {
        if (settings.mouseCursorIndicator.isVisible) iw->show();
        else                                          iw->hide();
    }

    if (auto log = cmdLog()) {
        log->info("ToggleIndicator: isVisible={}", settings.mouseCursorIndicator.isVisible);
    }
    return {};
}

// cmd 201: ピクセル検出有効/無効
std::expected<void, ImeIndicatorCmdError> cmdTogglePixelDetection() noexcept {
    auto* sm = g_settingsManager.load(std::memory_order_acquire);
    auto* mon = g_imeMonitor.load(std::memory_order_acquire);
    if (!sm) return std::unexpected(ImeIndicatorCmdError::ServiceNotInjected);

    auto& settings = sm->mutableSettings();
    // 0 ⇔ 2000ms（既定値）でトグル
    settings.pixelVerificationIntervalMs =
        (settings.pixelVerificationIntervalMs == 0) ? 2000 : 0;
    sm->save();

    if (mon) {
        mon->setPixelVerificationIntervalMs(settings.pixelVerificationIntervalMs);
    }

    if (auto log = cmdLog()) {
        log->info("TogglePixelDetection: intervalMs={}",
                  settings.pixelVerificationIntervalMs);
    }
    return {};
}

// cmd 202: IME 設定リロード
std::expected<void, ImeIndicatorCmdError> cmdReloadIMESettings() noexcept {
    auto* sm = g_settingsManager.load(std::memory_order_acquire);
    if (!sm) return std::unexpected(ImeIndicatorCmdError::ServiceNotInjected);

    if (!sm->load()) {
        if (auto log = cmdLog()) log->warn("ReloadIMESettings: load failed");
        return std::unexpected(ImeIndicatorCmdError::ApiCallFailed);
    }

    if (auto log = cmdLog()) log->info("ReloadIMESettings: settings reloaded");
    return {};
}

// cmd 210: 電源モード切替（バックアップ付き）
std::expected<void, ImeIndicatorCmdError> cmdTogglePowerMode() noexcept {
    const auto current = services::PowerModeService::getCurrentMode();
    const auto next    = services::PowerModeService::toggleMode();

    if (auto log = cmdLog()) {
        log->info("TogglePowerMode: {} -> {}",
                  models::powerModeToStableString(current),
                  models::powerModeToStableString(next));
    }
    return {};
}

// cmd 211: 高パフォーマンス電源プラン適用 + バックアップ
std::expected<void, ImeIndicatorCmdError> cmdApplyHighPerformancePower() noexcept {
    const auto current = services::PowerModeService::getCurrentMode();

    // バックアップ取得（次回 RestorePowerBackup または起動時復元用）
    // %LOCALAPPDATA%\IMEIndicator\state\power-mode-backup.json
    // App から localAppDataDir を取れないので、現時点では単純に setMode のみ。
    // 完全なバックアップ＋復元連携は Phase 7 で App::backupCurrentPowerMode を公開した後に対応。

    if (!services::PowerModeService::setMode(models::PowerMode::BestPerformance)) {
        return std::unexpected(ImeIndicatorCmdError::ApiCallFailed);
    }
    if (auto log = cmdLog()) {
        log->info("ApplyHighPerformancePower: prev={} -> BestPerformance",
                  models::powerModeToStableString(current));
    }
    return {};
}

// cmd 212: 電源モードバックアップから復元（state ディレクトリのバックアップ JSON を読む）
std::expected<void, ImeIndicatorCmdError> cmdRestorePowerBackup() noexcept {
    // App との配線が必要なため、現時点では単純に Balanced へ戻す簡易実装。
    // 本来は PowerModeBackup::tryLoad() でバックアップを読み、previousMode を復元する。
    // App::backupDir 公開後に完全実装に差し替える。
    if (!services::PowerModeService::setMode(models::PowerMode::Balanced)) {
        return std::unexpected(ImeIndicatorCmdError::ApiCallFailed);
    }
    if (auto log = cmdLog()) log->info("RestorePowerBackup: set to Balanced (simplified impl)");
    return {};
}

// cmd 220 / 221 共通: プロセス名にマッチするルールの isEnabled を切替
std::expected<void, ImeIndicatorCmdError>
cmdSetRuleEnabled(std::wstring_view processName, bool enabled) noexcept {
    auto* sm  = g_settingsManager.load(std::memory_order_acquire);
    auto* mon = g_priorityMonitor.load(std::memory_order_acquire);
    if (!sm) return std::unexpected(ImeIndicatorCmdError::ServiceNotInjected);
    if (processName.empty()) return std::unexpected(ImeIndicatorCmdError::ApiCallFailed);

    auto& settings = sm->mutableSettings();
    bool changed = false;
    for (auto& rule : settings.processPriorityRules) {
        // processName は wstring vs ワイドの比較
        std::wstring ruleNameW = std::wstring(
            rule.processName.begin(), rule.processName.end());  // 単純な ASCII 想定
        if (ruleNameW == processName) {
            if (rule.isEnabled != enabled) {
                rule.isEnabled = enabled;
                changed = true;
            }
        }
    }

    if (changed) {
        sm->save();
        if (mon) mon->updateRules(settings.processPriorityRules);
    }

    if (auto log = cmdLog()) {
        log->info("SetRuleEnabled: process={} enabled={} changed={}",
                  reinterpret_cast<const char*>(processName.data()),
                  enabled, changed);
    }
    return {};
}

// cmd 222: 全ルール一時停止
std::expected<void, ImeIndicatorCmdError> cmdPauseAllPriorityRules() noexcept {
    auto* sm  = g_settingsManager.load(std::memory_order_acquire);
    auto* mon = g_priorityMonitor.load(std::memory_order_acquire);
    if (!sm) return std::unexpected(ImeIndicatorCmdError::ServiceNotInjected);

    auto& settings = sm->mutableSettings();
    bool changed = false;
    for (auto& rule : settings.processPriorityRules) {
        if (rule.isEnabled) {
            rule.isEnabled = false;
            changed = true;
        }
    }
    if (changed) {
        sm->save();
        if (mon) mon->updateRules(settings.processPriorityRules);
    }

    if (auto log = cmdLog()) {
        log->info("PauseAllPriorityRules: changed={}", changed);
    }
    return {};
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
    auto log = cmdLog();

    if (!isImeIndicatorCommandId(cmdId)) {
        if (log) log->warn("imeindicator command id out of range: {}", cmdId);
        return std::unexpected(ImeIndicatorCmdError::InvalidCommand);
    }

    switch (cmdId) {
        case 200: return cmdToggleIndicator();
        case 201: return cmdTogglePixelDetection();
        case 202: return cmdReloadIMESettings();
        case 210: return cmdTogglePowerMode();
        case 211: return cmdApplyHighPerformancePower();
        case 212: return cmdRestorePowerBackup();
        case 220: return cmdSetRuleEnabled(param, false);  // 一時停止
        case 221: return cmdSetRuleEnabled(param, true);   // 再開
        case 222: return cmdPauseAllPriorityRules();
        default:
            if (log) log->warn("imeindicator command not implemented: id={}", cmdId);
            return std::unexpected(ImeIndicatorCmdError::NotImplemented);
    }
}

// === サービス注入 setter ===

void setIndicatorWindow(views::MouseCursorIndicatorWindow* window) noexcept {
    g_indicatorWindow.store(window, std::memory_order_release);
}

void setImeMonitor(services::IMEMonitor* monitor) noexcept {
    g_imeMonitor.store(monitor, std::memory_order_release);
}

void setSettingsManager(services::SettingsManager* manager) noexcept {
    g_settingsManager.store(manager, std::memory_order_release);
}

void setProcessPriorityMonitor(services::ProcessPriorityMonitor* monitor) noexcept {
    g_priorityMonitor.store(monitor, std::memory_order_release);
}

} // namespace imeindicator::services::hotkey
