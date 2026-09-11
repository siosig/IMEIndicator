#include "App.h"

#include "AppConstants.h"
#include "../services/IMEMonitor.h"
#include "../services/Logger.h"
#include "../services/PowerModeBackup.h"
#include "../services/PowerModeService.h"
#include "../services/ProcessPriorityService.h"
#include "../services/ProcessPriorityMonitor.h"
#include "../services/ColorHelper.h"
#include "../services/hotkey/HotkeyService.h"
#include "../services/hotkey/CommandExecutor.h"
#include "../services/hotkey/commands/ImeIndicatorCommands.h"
#include "../services/hotkey/commands/ProcessCommands.h"
#include "../views/MouseCursorIndicatorWindow.h"
#include "../views/BackgroundImageWindow.h"
#include "../views/TrayIcon.h"
#include "../views/SettingsDialog.h"
#include "../win32/NativeConstants.h"
#include "../win32/UnicodeUtil.h"

#include <span>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <shlobj.h>

#include <spdlog/spdlog.h>

#pragma comment(lib, "shell32.lib")

namespace imeindicator::app {
namespace {

constexpr wchar_t kMessageWindowClassName[] = L"IMEIndicator_MessageWindow";

std::filesystem::path resolveLocalAppDataDir()
{
    PWSTR raw = nullptr;
    HRESULT hr = ::SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &raw);
    if (FAILED(hr) || !raw) {
        if (raw) ::CoTaskMemFree(raw);
        wchar_t buf[MAX_PATH] = {};
        DWORD got = ::GetEnvironmentVariableW(L"LOCALAPPDATA", buf, MAX_PATH);
        if (got > 0 && got < MAX_PATH) {
            return std::filesystem::path(buf) /
                   std::filesystem::path(AppConstants::AppName);
        }
        return std::filesystem::current_path();
    }
    std::filesystem::path p(raw);
    ::CoTaskMemFree(raw);
    return p / std::filesystem::path(AppConstants::AppName);
}

} // namespace

App::App() = default;

App::~App()
{
    shutdown();
}

bool App::initialize(HINSTANCE hInstance)
{
    hInstance_ = hInstance;
    localAppDataDir_ = resolveLocalAppDataDir();

    settingsManager_.load();

    auto logsDir = localAppDataDir_ /
                   std::filesystem::path(AppConstants::LogsSubDir);
    services::Logger::init(logsDir, settingsManager_.settings().logLevel);

    if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
        log->info("App initialize: localAppData={}",
                  win32::wideToUtf8(localAppDataDir_.wstring()));
    }

    // PowerModeBackup の前回異常終了復元（FR-011）
    {
        auto stateDir = localAppDataDir_ /
                        std::filesystem::path(AppConstants::StateSubDir);
        services::PowerModeBackup backup(stateDir);
        if (auto rec = backup.tryLoad()) {
            services::PowerModeService::setMode(rec->previousMode);
            backup.deleteFile();
            if (auto log = spdlog::get(std::string(AppConstants::LoggerPower))) {
                log->info("Restored power mode from backup: {}",
                          models::powerModeToStableString(rec->previousMode));
            }
        }
    }

    if (!createMessageWindow(hInstance)) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
            log->error("createMessageWindow failed: {}", ::GetLastError());
        }
        return false;
    }

    // ---- インジケーターウィンドウ ----
    indicatorWindow_ = std::make_unique<views::MouseCursorIndicatorWindow>();
    if (!indicatorWindow_->initialize(hInstance)) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
            log->error("MouseCursorIndicatorWindow init failed");
        }
        return false;
    }
    indicatorWindow_->updateSettings(settingsManager_.settings().mouseCursorIndicator);
    indicatorWindow_->updateText(settingsManager_.settings().imeOnText);
    refreshIndicatorColor();

    // ---- 背景画像ウィンドウ（013-ime-corner-image） ----
    backgroundImageWindow_ = std::make_unique<views::BackgroundImageWindow>();
    if (!backgroundImageWindow_->initialize(hInstance)) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerDisplay))) {
            log->error("BackgroundImageWindow init failed (背景画像表示は無効のまま続行)");
        }
        backgroundImageWindow_.reset();
    }

    // ---- トレイアイコン ----
    trayIcon_ = std::make_unique<views::TrayIcon>();
    if (!trayIcon_->initialize(hInstance)) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
            log->error("TrayIcon init failed");
        }
        return false;
    }
    trayIcon_->setOpenSettingsCallback([this]() {
        if (settingsDialog_ && settingsDialog_->isOpen()) return;
        settingsDialog_ = std::make_unique<views::SettingsDialog>(settingsManager_);
        settingsDialog_->setAppliedCallback(
            [this](const models::AppSettings& s) {
                if (indicatorWindow_) {
                    indicatorWindow_->updateSettings(s.mouseCursorIndicator);
                    indicatorWindow_->updateText(s.imeOnText);
                }
                if (imeMonitor_) {
                    imeMonitor_->setPixelVerificationIntervalMs(
                        s.pixelVerificationIntervalMs);
                }
                services::Logger::setGlobalLevel(s.logLevel);
                if (priorityMonitor_) {
                    priorityMonitor_->updateRules(s.processPriorityRules);
                    priorityMonitor_->updatePollingInterval(s.pollingIntervalSeconds);
                }
                // ホットキー設定を反映（追加・削除・キー組合せ変更を即座に有効化する）。
                // reload() は HookEngine を一旦停止し HotkeyManager を新 settings で
                // 再ロードしてから HookEngine を再起動する。
                if (hotkeyService_) hotkeyService_->reload(s.hotkeySettings);
                // IME 状態に応じた表示再評価
                if (imeMonitor_) applyWindowVisibility(imeMonitor_->currentState());
            });
        settingsDialog_->setAccessDeniedCountCallback(
            [this]() -> int {
                return priorityService_ ? priorityService_->accessDeniedCount() : 0;
            });
        settingsDialog_->setAccessibilityProbeCallback(
            [this](const std::vector<models::ProcessPriorityRule>& rules)
                -> std::vector<bool> {
                std::vector<bool> blocked(rules.size(), false);
                if (!priorityService_) return blocked;
                for (size_t i = 0; i < rules.size(); ++i) {
                    if (!rules[i].isValid()) continue;
                    const std::wstring name = rules[i].normalizedProcessName();
                    blocked[i] = !priorityService_->isAccessibleForControl(name);
                }
                return blocked;
            });
        settingsDialog_->show(hInstance_);
        settingsDialog_.reset();
    });
    trayIcon_->setExitCallback([]() {
        ::PostQuitMessage(0);
    });
    trayIcon_->setToggleVisibleCallback([this](bool v) {
        setMouseIndicatorVisible(v);
    });
    trayIcon_->setSetPowerModeCallback([this](models::PowerMode mode) {
        backupCurrentPowerMode();
        services::PowerModeService::setMode(mode);
        refreshIndicatorColor();
        // tray-ui-contract.md は `/powertoggle 受信時のみ` のバルーン通知を規定するが、
        // メニュー経由で切り替えた際の視覚フィードバックが他に無いため
        // （IME OFF 中はインジケーター自体が非表示でカラー変化が見えない）
        // ユーザビリティを優先してメニュー経由でも通知を出す。
        if (trayIcon_) {
            std::wstring body = L"電源モード: ";
            body += services::PowerModeService::getDisplayName(mode);
            trayIcon_->showBalloon(L"IME Indicator", body);
        }
    });
    trayIcon_->setGetCurrentPowerModeCallback([]() {
        return services::PowerModeService::getCurrentMode();
    });
    trayIcon_->setGetIsVisibleCallback([this]() {
        return settingsManager_.settings().mouseCursorIndicator.isVisible;
    });
    // 013-ime-corner-image: 背景画像表示切替（カーソル追従インジケーターとは独立 / FR-016）。
    // 設定画面・トレイ・ホットキーのすべてが setBackgroundImageVisible に集約される。
    trayIcon_->setToggleBackgroundImageCallback([this](bool v) {
        setBackgroundImageVisible(v);
    });
    trayIcon_->setGetIsBackgroundImageVisibleCallback([this]() {
        return settingsManager_.settings().backgroundImage.isVisible;
    });
    // Phase 5 / US3: ホットキーサブメニュー連携
    trayIcon_->setGetTrayHotkeysCallback([this]() {
        return std::span<const models::hotkey::HotKeyEntry>{
            settingsManager_.settings().hotkeySettings.hotkeys
        };
    });
    trayIcon_->setExecuteHotkeyCallback([this](int hotkeyIndex) {
        if (!hotkeyService_) return;
        const auto& hotkeys = settingsManager_.settings().hotkeySettings.hotkeys;
        if (hotkeyIndex < 0 || static_cast<size_t>(hotkeyIndex) >= hotkeys.size()) return;
        const auto& hk = hotkeys[hotkeyIndex];
        // 内部コマンド or exe 起動を直接 dispatch（ホットキー押下と同じロジックを再利用したいが、
        // HotkeyService::onHotkeyDetected は private なので、ここでは executeCommandById /
        // launchApp を直接呼ぶ）
        if (hk.disable) return;
        if (hk.isCommand()) {
            (void)services::hotkey::executeCommandById(hk.cmd, hk.args, &hk);
        } else if (!hk.exe.empty()) {
            int nShow = 1;
            switch (hk.cmdShow) {
                case models::hotkey::WindowShow::Normal:    nShow = 1; break;
                case models::hotkey::WindowShow::Maximized: nShow = 3; break;
                case models::hotkey::WindowShow::Minimized: nShow = 2; break;
            }
            (void)services::hotkey::launchApp(hk.exe, hk.args, hk.dir,
                                              hk.admin, nShow);
        }
    });

    // ---- IMEMonitor ----
    imeMonitor_ = std::make_unique<services::IMEMonitor>();
    imeMonitor_->setPixelVerificationIntervalMs(
        settingsManager_.settings().pixelVerificationIntervalMs);
    imeMonitor_->setIMEStateCallback([this](const models::LanguageInfo& info) {
        onIMEStateChanged(info);
    });
    imeMonitor_->setCursorPositionCallback([this](int x, int y) {
        onCursorPositionChanged(x, y);
    });
    imeMonitor_->start();

    // ---- ProcessPriorityMonitor (Phase 5 / US3) ----
    priorityService_ = std::make_shared<services::ProcessPriorityService>();
    priorityMonitor_ = std::make_unique<services::ProcessPriorityMonitor>(priorityService_);
    {
        const auto& s = settingsManager_.settings();
        if (!s.processPriorityRules.empty()) {
            priorityMonitor_->start(s.processPriorityRules, s.pollingIntervalSeconds);
        }
    }

    startPowerToggleListener();

    // ---- HotkeyService (010-hotkeyp-merge / Phase 2-D + Phase 6 / US4) ----
    // HookEngine + HotkeyManager + CommandExecutor を統合し、AppSettings.hotkeySettings から
    // ホットキーをロード。failed should never block app startup（失敗時はホットキー無効で続行）。
    //
    // Phase 6 / US4: ImeIndicatorCommands に IMEIndicator 既存サービスのポインタを注入。
    // これにより cmd 200〜222（インジケーター切替・電源モード切替・優先度ルール制御）が
    // ホットキー経由で動作する。ポインタは shutdown() で nullptr に戻す。
    services::hotkey::setIndicatorWindow(indicatorWindow_.get());
    services::hotkey::setImeMonitor(imeMonitor_.get());
    services::hotkey::setSettingsManager(&settingsManager_);
    services::hotkey::setProcessPriorityMonitor(priorityMonitor_.get());
    // cmd 210（電源モード切替）が /toggle と同じ通知付き処理を実行するためのハンドラを注入。
    services::hotkey::setPowerModeToggleHandler(
        [this]() { togglePowerModeAndNotify(); });
    // cmd 203（背景画像表示切替）。設定更新・保存・現在の IME 状態での表示反映までを
    // App 側で一括処理する（013-ime-corner-image / FR-017）。
    services::hotkey::setBackgroundImageToggleHandler(
        [this]() { toggleBackgroundImageVisible(); });

    hotkeyService_ = std::make_unique<services::hotkey::HotkeyService>();
    if (!hotkeyService_->start(messageHwnd_,
                                settingsManager_.settings().hotkeySettings)) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerHotkey))) {
            log->warn("HotkeyService failed to start, hotkeys will be unavailable");
        }
    }

    // ---- 初回起動時に設定画面を自動表示 (Phase 4 / US2) ----
    if (settingsManager_.settings().isFirstLaunch) {
        auto s = settingsManager_.settings();
        s.isFirstLaunch = false;
        settingsManager_.setSettings(std::move(s));
        settingsManager_.save();
        // トレイアイコンの「設定」を開くのと同じ経路を起動完了後に発火させる。
        // 直接呼び出すと initialize 内で別メッセージループが回り composing が複雑化するため
        // メッセージ経由に固定する。
        ::PostMessageW(messageHwnd_, imeindicator::win32::WM_APP_OPEN_SETTINGS, 0, 0);
    }
    return true;
}

void App::shutdown()
{
    stopPowerToggleListener();

    if (priorityMonitor_) priorityMonitor_->stop();
    priorityMonitor_.reset();
    priorityService_.reset();

    if (imeMonitor_) imeMonitor_->stop();
    imeMonitor_.reset();

    // HotkeyService 停止（フックスレッド join + メインウィンドウハンドル解除）
    if (hotkeyService_) hotkeyService_->stop();
    hotkeyService_.reset();

    // Phase 6 / US4: ImeIndicatorCommands のサービス参照を解除（ダングリングポインタ回避）
    services::hotkey::setIndicatorWindow(nullptr);
    services::hotkey::setImeMonitor(nullptr);
    services::hotkey::setSettingsManager(nullptr);
    services::hotkey::setProcessPriorityMonitor(nullptr);
    services::hotkey::setPowerModeToggleHandler(nullptr);
    services::hotkey::setBackgroundImageToggleHandler(nullptr);

    settingsDialog_.reset();
    trayIcon_.reset();
    indicatorWindow_.reset();
    backgroundImageWindow_.reset();

    // 正常終了の証としてバックアップを削除（次回起動で復元発火を防ぐ）
    if (!localAppDataDir_.empty()) clearPowerModeBackup();

    if (messageHwnd_) {
        ::DestroyWindow(messageHwnd_);
        messageHwnd_ = nullptr;
    }
    if (messageWndClass_ && hInstance_) {
        ::UnregisterClassW(kMessageWindowClassName, hInstance_);
        messageWndClass_ = 0;
    }

    if (services::Logger::isInitialized()) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
            log->info("App shutdown");
        }
        services::Logger::shutdown();
    }
}

bool App::createMessageWindow(HINSTANCE hInstance)
{
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = &App::messageWndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = kMessageWindowClassName;
    messageWndClass_ = ::RegisterClassExW(&wc);
    if (!messageWndClass_) return false;

    messageHwnd_ = ::CreateWindowExW(
        0, kMessageWindowClassName, L"", 0,
        0, 0, 0, 0,
        HWND_MESSAGE, nullptr, hInstance, this);
    return messageHwnd_ != nullptr;
}

LRESULT CALLBACK App::messageWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        ::SetWindowLongPtrW(hwnd, GWLP_USERDATA,
                            reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
        return ::DefWindowProcW(hwnd, msg, wp, lp);
    }
    auto* self = reinterpret_cast<App*>(::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (self) return self->handleMessage(msg, wp, lp);
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT App::handleMessage(UINT msg, WPARAM wp, LPARAM lp)
{
    using namespace imeindicator::win32;

    // HotkeyP 由来の WM_HOTKEY_RAW_KBD / WM_HOTKEY_RAW_MOUSE を HotkeyService に転送。
    // services/hotkey/HookEngine が WH_KEYBOARD_LL / WH_MOUSE_LL から PostMessage する。
    if (hotkeyService_ && hotkeyService_->handleRawHookMessage(msg, wp, lp)) {
        return 0;
    }

    if (msg == WM_APP_IME_STATE_CHANGED) {
        models::LanguageInfo info{
            static_cast<models::LanguageType>(latestLanguage_.load()),
            latestImeOn_.load()
        };
        applyWindowVisibility(info);
        return 0;
    }
    if (msg == WM_APP_POWER_TOGGLE) {
        togglePowerModeAndNotify();
        return 0;
    }
    if (msg == WM_APP_OPEN_SETTINGS) {
        if (trayIcon_) {
            // トレイの open settings コールバックを再利用（重複防止つき）
            // 直接 settingsDialog を作るのではなく、トレイ経由のロジックを再利用。
            // 簡略化のため Tray の callback を直接呼ぶ。
            if (settingsDialog_ && settingsDialog_->isOpen()) return 0;
            settingsDialog_ = std::make_unique<views::SettingsDialog>(settingsManager_);
            settingsDialog_->setAppliedCallback(
                [this](const models::AppSettings& s) {
                    if (indicatorWindow_) {
                        indicatorWindow_->updateSettings(s.mouseCursorIndicator);
                        indicatorWindow_->updateText(s.imeOnText);
                    }
                    if (imeMonitor_) {
                        imeMonitor_->setPixelVerificationIntervalMs(
                            s.pixelVerificationIntervalMs);
                    }
                    services::Logger::setGlobalLevel(s.logLevel);
                    if (priorityMonitor_) {
                        priorityMonitor_->updateRules(s.processPriorityRules);
                        priorityMonitor_->updatePollingInterval(s.pollingIntervalSeconds);
                    }
                    // ホットキー設定を反映（追加・削除・キー組合せ変更を即時有効化）
                    if (hotkeyService_) hotkeyService_->reload(s.hotkeySettings);
                    if (imeMonitor_) applyWindowVisibility(imeMonitor_->currentState());
                });
            settingsDialog_->setAccessDeniedCountCallback(
                [this]() -> int {
                    return priorityService_ ? priorityService_->accessDeniedCount() : 0;
                });
            settingsDialog_->setAccessibilityProbeCallback(
                [this](const std::vector<models::ProcessPriorityRule>& rules)
                    -> std::vector<bool> {
                    std::vector<bool> blocked(rules.size(), false);
                    if (!priorityService_) return blocked;
                    for (size_t i = 0; i < rules.size(); ++i) {
                        if (!rules[i].isValid()) continue;
                        const std::wstring name = rules[i].normalizedProcessName();
                        blocked[i] = !priorityService_->isAccessibleForControl(name);
                    }
                    return blocked;
                });
            settingsDialog_->show(hInstance_);
            settingsDialog_.reset();
        }
        return 0;
    }
    return ::DefWindowProcW(messageHwnd_, msg, 0, 0);
}

int App::runMessageLoop()
{
    MSG msg{};
    while (::GetMessageW(&msg, nullptr, 0, 0) > 0) {
        ::TranslateMessage(&msg);
        ::DispatchMessageW(&msg);
    }
    return static_cast<int>(msg.wParam);
}

void App::onIMEStateChanged(const models::LanguageInfo& info)
{
    if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
        log->debug("App: onIMEStateChanged lang={} ime={}",
                   static_cast<int>(info.language), info.isImeOn);
    }
    // ワーカスレッドからの呼び出しのため、メインメッセージスレッドへマーシャリング。
    latestLanguage_.store(static_cast<int>(info.language));
    latestImeOn_.store(info.isImeOn);
    if (messageHwnd_) {
        ::PostMessageW(messageHwnd_,
                       imeindicator::win32::WM_APP_IME_STATE_CHANGED, 0, 0);
    }
}

void App::onCursorPositionChanged(int x, int y)
{
    // MouseTracker は SetTimer ベースで UI スレッドから呼ばれるため、直接更新可能。
    if (indicatorWindow_) indicatorWindow_->updatePosition(x, y);
}

void App::applyWindowVisibility(const models::LanguageInfo& info)
{
    const bool imeOn = (info.language == models::LanguageType::Japanese) && info.isImeOn;

    if (indicatorWindow_) {
        const auto& cfg = settingsManager_.settings().mouseCursorIndicator;
        if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
            log->debug("App: applyWindowVisibility lang={} ime={} cfgVisible={}",
                       static_cast<int>(info.language), info.isImeOn, cfg.isVisible);
        }
        if (!cfg.isVisible) {
            indicatorWindow_->hide();
        } else if (imeOn) {
            indicatorWindow_->show();
        } else {
            indicatorWindow_->hide();
        }
    }

    // 013-ime-corner-image: 背景画像は「インジケーター表示」と完全に独立（FR-009）。
    // cfg.isVisible の早期 return に巻き込まれないよう、ここで必ず評価する。
    applyBackgroundImageVisibility(imeOn);
}

void App::applyBackgroundImageVisibility(bool imeOn)
{
    if (!backgroundImageWindow_) return;
    const bool shouldShow = settingsManager_.settings().backgroundImage.isVisible && imeOn;
    if (shouldShow) backgroundImageWindow_->show();
    else            backgroundImageWindow_->hide();
}

void App::refreshIndicatorColor()
{
    if (!indicatorWindow_) return;
    const auto mode = services::PowerModeService::getCurrentMode();
    const auto hex = services::PowerModeService::getIndicatorColorHex(mode);
    indicatorWindow_->updateColor(services::ColorHelper::parseColor(hex));
}

void App::setMouseIndicatorVisible(bool visible)
{
    auto s = settingsManager_.settings();
    s.mouseCursorIndicator.isVisible = visible;
    settingsManager_.setSettings(std::move(s));
    settingsManager_.save();

    if (visible) {
        if (imeMonitor_) applyWindowVisibility(imeMonitor_->currentState());
        else if (indicatorWindow_) indicatorWindow_->show();
    } else if (indicatorWindow_) {
        indicatorWindow_->hide();
    }
}

void App::setBackgroundImageVisible(bool visible)
{
    auto s = settingsManager_.settings();
    s.backgroundImage.isVisible = visible;
    settingsManager_.setSettings(std::move(s));
    settingsManager_.save();

    if (imeMonitor_) applyWindowVisibility(imeMonitor_->currentState());
    else if (backgroundImageWindow_) backgroundImageWindow_->hide();
}

void App::toggleBackgroundImageVisible()
{
    setBackgroundImageVisible(!settingsManager_.settings().backgroundImage.isVisible);
}

void App::togglePowerModeAndNotify()
{
    backupCurrentPowerMode();
    const auto next = services::PowerModeService::toggleMode();
    refreshIndicatorColor();
    if (trayIcon_) {
        std::wstring body = L"電源モード: ";
        body += services::PowerModeService::getDisplayName(next);
        trayIcon_->showBalloon(L"IME Indicator", body);
    }
}

void App::backupCurrentPowerMode()
{
    auto stateDir = localAppDataDir_ /
                    std::filesystem::path(AppConstants::StateSubDir);
    services::PowerModeBackup backup(stateDir);
    services::PowerModeBackupRecord rec{};
    rec.previousMode = services::PowerModeService::getCurrentMode();
    backup.save(rec);
}

void App::clearPowerModeBackup()
{
    auto stateDir = localAppDataDir_ /
                    std::filesystem::path(AppConstants::StateSubDir);
    services::PowerModeBackup backup(stateDir);
    backup.deleteFile();
}

void App::startPowerToggleListener()
{
    powerToggleStop_.store(false);
    powerToggleStopEvent_ = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
    powerToggleEvent_ = ::CreateEventW(
        nullptr, FALSE, FALSE,
        std::wstring(AppConstants::PowerToggleEventName).c_str());
    if (!powerToggleEvent_ || !powerToggleStopEvent_) return;

    powerToggleThread_ = std::thread([this]() {
        HANDLE handles[2] = { powerToggleEvent_, powerToggleStopEvent_ };
        for (;;) {
            DWORD rc = ::WaitForMultipleObjects(2, handles, FALSE, INFINITE);
            if (rc != WAIT_OBJECT_0) break;
            if (powerToggleStop_.load()) break;
            if (messageHwnd_) {
                ::PostMessageW(messageHwnd_,
                               imeindicator::win32::WM_APP_POWER_TOGGLE, 0, 0);
            }
        }
    });
}

void App::stopPowerToggleListener()
{
    powerToggleStop_.store(true);
    if (powerToggleStopEvent_) ::SetEvent(powerToggleStopEvent_);
    if (powerToggleThread_.joinable()) powerToggleThread_.join();
    if (powerToggleEvent_) {
        ::CloseHandle(powerToggleEvent_);
        powerToggleEvent_ = nullptr;
    }
    if (powerToggleStopEvent_) {
        ::CloseHandle(powerToggleStopEvent_);
        powerToggleStopEvent_ = nullptr;
    }
}

} // namespace imeindicator::app
