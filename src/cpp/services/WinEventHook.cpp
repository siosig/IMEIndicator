#include "WinEventHook.h"

namespace imeindicator::services {

WinEventHook& WinEventHook::instance() noexcept
{
    static WinEventHook s;
    return s;
}

WinEventHook::~WinEventHook()
{
    stop();
}

bool WinEventHook::start()
{
    if (hook_) return true;
    hook_ = ::SetWinEventHook(
        EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
        nullptr,
        &WinEventHook::winEventProc,
        0, 0,
        WINEVENT_OUTOFCONTEXT);
    return hook_ != nullptr;
}

void WinEventHook::stop() noexcept
{
    if (hook_) {
        ::UnhookWinEvent(hook_);
        hook_ = nullptr;
    }
}

void CALLBACK WinEventHook::winEventProc(HWINEVENTHOOK, DWORD, HWND,
                                          LONG, LONG, DWORD, DWORD)
{
    auto& self = instance();
    if (self.callback_) self.callback_();
}

} // namespace imeindicator::services
