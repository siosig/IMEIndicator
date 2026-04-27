#include "App.h"

#include "AppConstants.h"
#include "../services/Logger.h"
#include "../services/PowerModeBackup.h"
#include "../win32/UnicodeUtil.h"

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

std::filesystem::path localAppDataDir()
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
    localAppDataDir_ = localAppDataDir();

    // 設定読み込み（ログ初期化前なのでログ無し）
    settingsManager_.load();

    // ロガー初期化
    auto logsDir = localAppDataDir_ /
                   std::filesystem::path(AppConstants::LogsSubDir);
    services::Logger::init(logsDir, settingsManager_.settings().logLevel);

    if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
        log->info("App initialize: localAppData={}",
                  win32::wideToUtf8(localAppDataDir_.wstring()));
    }

    // PowerModeBackup の前回異常終了復元（FR-011）
    auto stateDir = localAppDataDir_ /
                    std::filesystem::path(AppConstants::StateSubDir);
    services::PowerModeBackup backup(stateDir);
    if (auto rec = backup.tryLoad()) {
        // Phase 2 では PowerModeService が未実装のため、復元は Phase 5 で配線する。
        // ここではバックアップを残したまま、Phase 5 で起動シーケンスから再評価される。
        if (auto log = spdlog::get(std::string(AppConstants::LoggerPower))) {
            log->warn("Found power-mode backup (mode={}, savedAtMs={}); "
                      "deferring restore to ProcessPriorityMonitor (Phase 5)",
                      models::powerModeToStableString(rec->previousMode),
                      rec->savedAtUnixMs);
        }
    }

    if (!createMessageWindow(hInstance)) {
        if (auto log = spdlog::get(std::string(AppConstants::LoggerApp))) {
            log->error("createMessageWindow failed: {}", ::GetLastError());
        }
        return false;
    }

    return true;
}

void App::shutdown()
{
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

    // HWND_MESSAGE で非表示メッセージ専用ウィンドウを作る
    messageHwnd_ = ::CreateWindowExW(
        0,
        kMessageWindowClassName,
        L"",
        0,
        0, 0, 0, 0,
        HWND_MESSAGE,
        nullptr,
        hInstance,
        this);
    return messageHwnd_ != nullptr;
}

LRESULT CALLBACK App::messageWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    // Phase 2 では空のメッセージ処理。Phase 3 以降で WM_APP_TRAY_NOTIFY 等を処理する。
    return ::DefWindowProcW(hwnd, msg, wParam, lParam);
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

} // namespace imeindicator::app
