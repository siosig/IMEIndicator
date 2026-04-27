#include "IMEMonitor.h"

#include "IMEDetector.h"
#include "KeyboardHook.h"
#include "MouseTracker.h"
#include "PixelIMEDetector.h"
#include "WinEventHook.h"

#include "../app/AppConstants.h"

#include <spdlog/spdlog.h>

#include <chrono>
#include <memory>

#define IME_LOG(...) \
    do { if (auto _log = spdlog::get(std::string(imeindicator::app::AppConstants::LoggerIme))) _log->debug(__VA_ARGS__); } while(0)

namespace imeindicator::services {

namespace {

uint64_t nowMs() noexcept
{
    return ::GetTickCount64();
}

// FILETIME に「相対時間（負の 100ns 単位）」を設定するヘルパー。
// SetThreadpoolTimer は ULARGE_INTEGER の負値で「未来の相対時刻」を表現する。
FILETIME relativeFiletimeFromMs(int ms) noexcept
{
    // 100 ns 単位、負値で相対時刻
    LONGLONG due = -static_cast<LONGLONG>(ms) * 10'000LL;
    FILETIME ft{};
    ft.dwLowDateTime = static_cast<DWORD>(due & 0xFFFFFFFFULL);
    ft.dwHighDateTime = static_cast<DWORD>(due >> 32);
    return ft;
}

} // namespace

IMEMonitor::IMEMonitor() = default;

IMEMonitor::~IMEMonitor()
{
    stop();
}

void IMEMonitor::setPixelVerificationIntervalMs(int ms) noexcept
{
    pixelVerificationIntervalMs_ = ms;
}

models::LanguageInfo IMEMonitor::currentState() const noexcept
{
    std::lock_guard lock(stateMutex_);
    return lastState_;
}

bool IMEMonitor::start()
{
    if (running_.exchange(true)) return true;

    // デバウンスタイマー（手動再アーム用にスレッドプールタイマーを 1 個確保）
    debounceTimer_ = ::CreateThreadpoolTimer(&IMEMonitor::debounceTimerCallback, this, nullptr);
    if (!debounceTimer_) {
        running_.store(false);
        return false;
    }

    // KeyboardHook（シングルトン）にコールバックを登録 → 起動
    auto& kbd = KeyboardHook::instance();
    kbd.setImeKeyCallback([this](int vk) { onIMEKeyPressed(vk); });
    kbd.setLanguageSwitchCallback([this]() { onLanguageSwitchDetected(); });
    bool kbdOk = kbd.start();
    IME_LOG("IMEMonitor: KeyboardHook.start={}", kbdOk);

    // WinEventHook（シングルトン）でフォアグラウンド変更を購読
    auto& we = WinEventHook::instance();
    we.setFocusChangedCallback([this]() { onTriggerFired(); });
    bool weOk = we.start();
    IME_LOG("IMEMonitor: WinEventHook.start={}", weOk);

    // MouseTracker は所有
    mouseTracker_ = std::make_unique<MouseTracker>();
    mouseTracker_->setCallback([this](int x, int y) {
        if (cursorPositionCallback_) cursorPositionCallback_(x, y);
    });
    bool mtOk = mouseTracker_->start();
    IME_LOG("IMEMonitor: MouseTracker.start={}", mtOk);

    // 初回チェック
    onTriggerFired();
    return true;
}

void IMEMonitor::stop() noexcept
{
    if (!running_.exchange(false)) return;

    // タイマーを停止して未発火のコールバックを待機
    if (debounceTimer_) {
        ::SetThreadpoolTimer(debounceTimer_, nullptr, 0, 0);
        ::WaitForThreadpoolTimerCallbacks(debounceTimer_, TRUE);
        ::CloseThreadpoolTimer(debounceTimer_);
        debounceTimer_ = nullptr;
    }

    // フックは singleton なのでコールバックだけ外し、他クライアントも止めるなら stop()
    auto& kbd = KeyboardHook::instance();
    kbd.setImeKeyCallback(nullptr);
    kbd.setLanguageSwitchCallback(nullptr);
    kbd.stop();

    auto& we = WinEventHook::instance();
    we.setFocusChangedCallback(nullptr);
    we.stop();

    if (mouseTracker_) {
        mouseTracker_->stop();
        mouseTracker_.reset();
    }
}

void IMEMonitor::onTriggerFired()
{
    if (!running_.load() || !debounceTimer_) return;
    auto due = relativeFiletimeFromMs(kDebounceDelayMs);
    ::SetThreadpoolTimer(debounceTimer_, &due, 0, 0);
}

void CALLBACK IMEMonitor::debounceTimerCallback(PTP_CALLBACK_INSTANCE,
                                                PVOID context,
                                                PTP_TIMER)
{
    auto* self = static_cast<IMEMonitor*>(context);
    if (self) self->onDebounceFired();
}

void IMEMonitor::onDebounceFired()
{
    if (!running_.load()) return;
    checkIMEState(false);
}

void IMEMonitor::checkIMEState(bool forceUpdate)
{
    // 多重実行防止（C# 版の Interlocked.Exchange と同等）
    int expected = 0;
    if (!isChecking_.compare_exchange_strong(expected, 1)) return;

    struct Releaser {
        std::atomic<int>& flag;
        ~Releaser() { flag.store(0); }
    } releaser{isChecking_};

    HWND hwndForeground = ::GetForegroundWindow();
    if (!hwndForeground) {
        IME_LOG("IMEMonitor: checkIMEState skipped (no foreground window)");
        return;
    }

    auto detection = IMEDetector::getCurrentIMEStateEx(trackedLanguageForTerminal_);
    auto state = detection.state;

    bool windowChanged = false;
    {
        std::lock_guard lock(stateMutex_);
        windowChanged = (hwndForeground != lastForegroundWindow_);
    }

    // 日本語ならピクセル判定で答え合わせ
    if (state.language == models::LanguageType::Japanese) {
        auto px = PixelIMEDetector::instance().detectIMEStateTimed(state.language);
        if (px.isOn.has_value()) {
            state.isImeOn = *px.isOn;
            std::lock_guard lock(stateMutex_);
            trackedIMEState_ = *px.isOn;
        }
    }

    if (windowChanged) {
        std::lock_guard lock(stateMutex_);
        lastForegroundWindow_ = hwndForeground;
    }

    // 楽観的更新の検証窓内（200ms）かつ状態一致なら発火しない
    bool shouldFire = false;
    {
        std::lock_guard lock(stateMutex_);
        const bool stateChanged = (state != lastState_);
        if (stateChanged || forceUpdate) {
            const uint64_t elapsed = nowMs() - optimisticUpdateTimestamp_.load();
            const bool withinOptimisticWindow =
                elapsed < static_cast<uint64_t>(kVerificationDelayMs);
            if (withinOptimisticWindow && !stateChanged) {
                // 一致 → 何もしない
            } else {
                lastState_ = state;
                shouldFire = true;
            }
        }
    }

    if (shouldFire && imeStateCallback_) {
        imeStateCallback_(state);
    }
}

void IMEMonitor::onIMEKeyPressed(int vkCode)
{
    constexpr int kVkKanji = 0x19;
    constexpr int kVkOemAuto = 0xF3;
    constexpr int kVkOemEnlw = 0xF4;
    IME_LOG("IMEMonitor: onIMEKeyPressed vk=0x{:02x}", vkCode);

    bool stateChanged = false;
    models::LanguageInfo optimisticState{};

    {
        std::lock_guard lock(stateMutex_);
        if (vkCode == kVkKanji) {
            trackedIMEState_ = !trackedIMEState_;
            stateChanged = true;
        } else if (vkCode == kVkOemAuto) {
            trackedIMEState_ = false;
            trackedLanguageForTerminal_ = models::LanguageType::Japanese;
            stateChanged = true;
        } else if (vkCode == kVkOemEnlw) {
            trackedIMEState_ = true;
            trackedLanguageForTerminal_ = models::LanguageType::Japanese;
            stateChanged = true;
        }

        if (stateChanged) {
            optimisticState = {models::LanguageType::Japanese, trackedIMEState_};
            lastState_ = optimisticState;
            optimisticUpdateTimestamp_.store(nowMs());
        }
    }

    if (stateChanged) {
        if (imeStateCallback_) imeStateCallback_(optimisticState);

        // 200ms 後にピクセル判定で答え合わせ
        if (debounceTimer_) {
            auto due = relativeFiletimeFromMs(kVerificationDelayMs);
            ::SetThreadpoolTimer(debounceTimer_, &due, 0, 0);
        }
    }
}

void IMEMonitor::onLanguageSwitchDetected()
{
    HWND hwnd = ::GetForegroundWindow();
    if (!hwnd) return;

    DWORD threadId = ::GetWindowThreadProcessId(hwnd, nullptr);
    HKL hkl = ::GetKeyboardLayout(threadId);
    const int langId = static_cast<int>(reinterpret_cast<INT_PTR>(hkl)) & 0xFFFF;

    {
        std::lock_guard lock(stateMutex_);
        trackedLanguageForTerminal_ = IMEDetector::getLanguageType(langId);
    }
    checkIMEState(true);
}

} // namespace imeindicator::services
