#include "MouseTracker.h"

namespace imeindicator::services {
namespace {

constexpr wchar_t kClassName[] = L"IMEIndicator_MouseTrackerWindow";

LRESULT CALLBACK trackerWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_TIMER) {
        auto* self = reinterpret_cast<MouseTracker*>(
            ::GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (self) self->onTimerTick();
        return 0;
    }
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

ATOM ensureClassRegistered()
{
    static ATOM cls = []() {
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = &trackerWndProc;
        wc.hInstance = ::GetModuleHandleW(nullptr);
        wc.lpszClassName = kClassName;
        return ::RegisterClassExW(&wc);
    }();
    return cls;
}

} // namespace

MouseTracker::~MouseTracker()
{
    stop();
}

bool MouseTracker::start(unsigned intervalMs)
{
    if (timerWindow_) return true;
    if (!ensureClassRegistered()) return false;

    timerWindow_ = ::CreateWindowExW(0, kClassName, L"", 0, 0, 0, 0, 0,
                                     HWND_MESSAGE, nullptr,
                                     ::GetModuleHandleW(nullptr), nullptr);
    if (!timerWindow_) return false;

    ::SetWindowLongPtrW(timerWindow_, GWLP_USERDATA,
                        reinterpret_cast<LONG_PTR>(this));
    timerId_ = ::SetTimer(timerWindow_, 1, intervalMs, nullptr);
    if (!timerId_) {
        ::DestroyWindow(timerWindow_);
        timerWindow_ = nullptr;
        return false;
    }
    ::GetCursorPos(&lastPos_);
    // 初回コールバックを即時発火する。差分検出ベースのため、これがないと
    // ユーザーがマウスを動かすまでウィンドウが (0,0) に固定されてしまう。
    if (callback_) callback_(lastPos_.x, lastPos_.y);
    return true;
}

void MouseTracker::stop() noexcept
{
    if (timerWindow_) {
        if (timerId_) {
            ::KillTimer(timerWindow_, timerId_);
            timerId_ = 0;
        }
        ::DestroyWindow(timerWindow_);
        timerWindow_ = nullptr;
    }
}

void MouseTracker::onTimerTick()
{
    POINT cur;
    if (!::GetCursorPos(&cur)) return;
    if (cur.x != lastPos_.x || cur.y != lastPos_.y) {
        lastPos_ = cur;
        if (callback_) callback_(cur.x, cur.y);
    }
}

} // namespace imeindicator::services
