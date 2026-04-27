// IMEIndicator wWinMain
// - DPI awareness 設定 (PerMonitorV2)
// - COM 初期化 (APARTMENTTHREADED)
// - /powertoggle 短経路
// - シングルインスタンス Mutex
// - メッセージループ呼び出し

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <shellapi.h>           // CommandLineToArgvW
#include <shellscalingapi.h>

#include <cwchar>
#include <string>
#include <string_view>

#include "App.h"
#include "AppConstants.h"
#include "EntryPoint_PowerToggle.h"
#include "../win32/ComUtil.h"

#pragma comment(lib, "shcore.lib")

namespace {

// 大小無視で先頭引数が "/powertoggle" であるかを判定
bool isPowerToggleArg(LPCWSTR cmdLine)
{
    if (!cmdLine || !*cmdLine) return false;

    int argc = 0;
    LPWSTR* argv = ::CommandLineToArgvW(cmdLine, &argc);
    if (!argv) return false;

    bool result = false;
    if (argc >= 1) {
        constexpr std::wstring_view kFlag = L"/powertoggle";
        std::wstring_view first(argv[0]);
        if (first.size() == kFlag.size()) {
            result = (::CompareStringOrdinal(first.data(), static_cast<int>(first.size()),
                                             kFlag.data(),  static_cast<int>(kFlag.size()),
                                             TRUE) == CSTR_EQUAL);
        }
    }
    ::LocalFree(argv);
    return result;
}

} // namespace

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE /*hPrevInstance*/,
                      _In_ LPWSTR lpCmdLine,
                      _In_ int /*nCmdShow*/)
{
    // ---- DPI awareness ----
    // app.manifest 側でも PerMonitorV2 を宣言済みだが、明示呼び出しでフォールバック確実化。
    if (!::SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) {
        // 古い API 経由で再試行
        ::SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
    }

    // ---- /powertoggle 短経路 ----
    if (isPowerToggleArg(lpCmdLine)) {
        return imeindicator::app::runPowerToggleEntry();
    }

    // ---- COM 初期化 ----
    imeindicator::win32::ComApartment com{};
    if (!com.ok()) {
        return 1;
    }

    // ---- シングルインスタンス Mutex ----
    HANDLE hMutex = ::CreateMutexW(
        nullptr, TRUE,
        std::wstring(imeindicator::app::AppConstants::MutexName).c_str());
    if (!hMutex || ::GetLastError() == ERROR_ALREADY_EXISTS) {
        if (hMutex) ::CloseHandle(hMutex);
        return 0;  // 二重起動は静かに終了
    }

    int exitCode = 0;
    {
        imeindicator::app::App app;
        if (!app.initialize(hInstance)) {
            exitCode = 1;
        } else {
            exitCode = app.runMessageLoop();
        }
    }

    if (hMutex) {
        ::ReleaseMutex(hMutex);
        ::CloseHandle(hMutex);
    }

    return exitCode;
}
