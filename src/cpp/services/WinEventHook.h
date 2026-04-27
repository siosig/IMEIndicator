#pragma once

#include <functional>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// EVENT_SYSTEM_FOREGROUND（フォアグラウンドウィンドウ切替）の検出。
// 既存 [WinEventHookManager.cs] と等価。EVENT_OBJECT_FOCUS は廃止
// （アプリ内コントロール間遷移で大量発火しデバウンスを浪費するため）。
//
// SetWinEventHook の WINEVENTPROC コールバックは context を受け取れないため、
// プロセスごとのシングルトンとして実装する。
class WinEventHook {
public:
    using FocusChangedCallback = std::function<void()>;

    static WinEventHook& instance() noexcept;

    void setFocusChangedCallback(FocusChangedCallback cb) { callback_ = std::move(cb); }

    bool start();
    void stop() noexcept;

    bool isRunning() const noexcept { return hook_ != nullptr; }

private:
    WinEventHook() = default;
    ~WinEventHook();
    WinEventHook(const WinEventHook&) = delete;
    WinEventHook& operator=(const WinEventHook&) = delete;

    static void CALLBACK winEventProc(HWINEVENTHOOK, DWORD, HWND,
                                      LONG, LONG, DWORD, DWORD);

    HWINEVENTHOOK hook_{nullptr};
    FocusChangedCallback callback_;
};

} // namespace imeindicator::services
